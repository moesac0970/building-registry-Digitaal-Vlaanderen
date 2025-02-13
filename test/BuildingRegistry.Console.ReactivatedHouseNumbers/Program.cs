namespace BuildingRegistry.Console.ReactivatedHouseNumbers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Mime;
    using System.Text;
    using System.Threading.Tasks;
    using Api.CrabImport.Abstractions.Post;
    using Be.Vlaanderen.Basisregisters.Crab;
    using Be.Vlaanderen.Basisregisters.EventHandling;
    using Be.Vlaanderen.Basisregisters.GrAr.Import.Processing.Json;
    using Dapper;
    using Legacy;
    using Legacy.Commands.Crab;
    using Legacy.Crab;
    using Legacy.Events.Crab;
    using Microsoft.Data.SqlClient;
    using Microsoft.Extensions.Configuration;
    using Newtonsoft.Json;
    using NodaTime;

    //Case 5f: when housenumber relation is retired then becomes active again (correction), buildings with more than 2 subaddresses don't have the right amount of units
    public class Program
    {
        const string FilesToProcessPath = "FilesToProcess";

        static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile($"appsettings.{Environment.MachineName.ToLowerInvariant()}.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .AddCommandLine(args ?? new string[0])
                .Build();

            var eventsConnectionString = configuration.GetConnectionString("Events");
            var eventsJsonSerializerSettings = new JsonSerializerSettings().ConfigureDefaultForEvents();
            var commandsJsonSerializerSettings = new JsonSerializerSettings().ConfigureForCrabImports();

            var appSettings = new ApplicationSettings(configuration.GetSection("ApplicationSettings"));

            Directory.CreateDirectory(FilesToProcessPath);

            var lastStreamId = -1;
            var streamsIds = GetNextStreamsIds(eventsConnectionString, lastStreamId);

            do
            {
                Console.WriteLine($"{streamsIds.First()} - {streamsIds.Last()}");

                Parallel.ForEach(streamsIds, streamId =>
                {
                    using (var sqlConnection = new SqlConnection(eventsConnectionString))
                    {
                        var importedTerrainObjectHouseNumbers = GetImportedTerrainObjectHouseNumbers(sqlConnection, streamId, eventsJsonSerializerSettings);
                        var importedReaddressedHouseNumbers = GetReaddressedHouseNumbersByStream(sqlConnection, streamId, eventsJsonSerializerSettings);

                        var nonDeletedTerrainObjectHouseNumbersGroupedById = importedTerrainObjectHouseNumbers
                            .GroupBy(x => x.TerrainObjectHouseNumberId)
                            .Where(x => x.All(y => y.Modification != CrabModification.Delete))
                            .ToDictionary(x => x.Key, x => x.ToList());

                        foreach (var terrainObjectHouseNumberWasImportedFromCrab in nonDeletedTerrainObjectHouseNumbersGroupedById)
                        {
                            var hasEndDate = false;
                            var timestampSinceLastReactivation = Instant.MinValue;
                            foreach (var terrainObjectHouseNumber in terrainObjectHouseNumberWasImportedFromCrab.Value.OrderBy(x => x.Timestamp))
                            {
                                if ((hasEndDate && !terrainObjectHouseNumber.EndDateTime.HasValue) || HasReaddressedEnded(importedReaddressedHouseNumbers, terrainObjectHouseNumber, nonDeletedTerrainObjectHouseNumbersGroupedById)) //was retired, now not anymore
                                {
                                    var subaddresses = GetSubaddressesByStreamAndTerrainObjectHouseNumber(sqlConnection, streamId, terrainObjectHouseNumber.TerrainObjectHouseNumberId, eventsJsonSerializerSettings);
                                    var readdressedSubaddresses = GetReaddressedSubaddressesByStream(sqlConnection, streamId, terrainObjectHouseNumber, eventsJsonSerializerSettings);

                                    if (readdressedSubaddresses.Any())
                                    {
                                        //combine new subaddresses with old (fused)
                                        subaddresses = subaddresses
                                            .Union(GetSubaddressesByStreamAndTerrainObjectHouseNumber(
                                                    sqlConnection,
                                                    streamId,
                                                    readdressedSubaddresses.First().OldTerrainObjectHouseNumberId,
                                                    eventsJsonSerializerSettings))
                                            .ToList();
                                    }

                                    var deletedSubaddresses = subaddresses
                                        .Where(subaddress => subaddress.Modification == CrabModification.Delete)
                                        .Select(x => x.SubaddressId)
                                        .Distinct()
                                        .ToList();

                                    var oldDeletedSubaddresses = new List<int>();
                                    //if new one is deleted, then old one shouldn't be reactivated either
                                    foreach (var deletedSubaddress in deletedSubaddresses)
                                    {
                                        var subaddressWasReaddressedFromCrab = readdressedSubaddresses.FirstOrDefault(x => x.NewSubaddressId == deletedSubaddress);
                                        if (subaddressWasReaddressedFromCrab != null)
                                            oldDeletedSubaddresses.Add(subaddressWasReaddressedFromCrab.OldSubaddressId);
                                    }

                                    deletedSubaddresses = deletedSubaddresses.Union(oldDeletedSubaddresses).ToList();

                                    var sinceLastReactivation = timestampSinceLastReactivation;
                                    var nonDeletedSubaddresses = subaddresses
                                        .Where(subaddress =>
                                                    subaddress.Timestamp < terrainObjectHouseNumber.Timestamp &&
                                                    subaddress.Timestamp > sinceLastReactivation)
                                        .Select(subaddress => subaddress.SubaddressId)
                                        .Distinct()
                                        .Except(deletedSubaddresses)
                                        .ToList();

                                    CreateCommandsIfNecessary(nonDeletedSubaddresses, subaddresses, readdressedSubaddresses, subaddresses, commandsJsonSerializerSettings);

                                    hasEndDate = false;
                                    timestampSinceLastReactivation = terrainObjectHouseNumber.Timestamp;
                                }

                                if (terrainObjectHouseNumber.EndDateTime.HasValue) //need timestamp first occurred
                                    hasEndDate = true;
                            }
                        }
                    }
                });

                lastStreamId = streamsIds.Last();
                streamsIds = GetNextStreamsIds(eventsConnectionString, lastStreamId).ToList();

            } while (streamsIds.Any());

            ReadFilesAndSendCommands(commandsJsonSerializerSettings, appSettings);
        }

        private static bool HasReaddressedEnded(
            List<HouseNumberWasReaddressedFromCrab> importedReaddressedHouseNumbers,
            TerrainObjectHouseNumberWasImportedFromCrab terrainObjectHouseNumber,
            Dictionary<int, List<TerrainObjectHouseNumberWasImportedFromCrab>> nonDeletedTerrainObjectHouseNumbersGroupedById)
        {
            var readdressedHouseNumber = importedReaddressedHouseNumbers.FirstOrDefault(x => x.NewTerrainObjectHouseNumberId == terrainObjectHouseNumber.TerrainObjectHouseNumberId);
            return readdressedHouseNumber != null &&
                                      nonDeletedTerrainObjectHouseNumbersGroupedById.ContainsKey(readdressedHouseNumber.OldTerrainObjectHouseNumberId) &&
                                      nonDeletedTerrainObjectHouseNumbersGroupedById[readdressedHouseNumber.OldTerrainObjectHouseNumberId].Any(x => x.EndDateTime.HasValue);
        }

        private static void ReadFilesAndSendCommands(
            JsonSerializerSettings commandsJsonSerializerSettings,
            ApplicationSettings appSettings)
        {
            var processedPath = "Processed";
            Directory.CreateDirectory(processedPath);

            foreach (var file in Directory.GetFiles(FilesToProcessPath))
            {
                var commandsToSend = new List<RegisterCrabImportRequest[]>();
                var command = JsonConvert.DeserializeObject<RegisterCrabImportRequest>(File.ReadAllText(file), commandsJsonSerializerSettings);

                commandsToSend.Add(new[] { command });

                SendCommands(commandsToSend, commandsJsonSerializerSettings, appSettings);

                File.Move(file, Path.Combine(processedPath, Path.GetFileName(file)));
            }
        }

        private static void CreateCommandsIfNecessary(
            List<int> nonDeletedSubaddresses,
            List<AddressSubaddressWasImportedFromCrab> subaddresses,
            List<SubaddressWasReaddressedFromCrab> readdressedSubaddresses,
            List<AddressSubaddressWasImportedFromCrab> allSubaddresses,
            JsonSerializerSettings commandsJsonSerializerSettings)
        {
            if (nonDeletedSubaddresses.Count > 2)
            {
                var latestCommands = new List<ImportSubaddressFromCrab>();
                foreach (var nonDeletedSubaddressId in nonDeletedSubaddresses)
                {
                    var lastImportedCommand =
                        subaddresses
                            .Where(x => x.SubaddressId == nonDeletedSubaddressId)
                            .OrderBy(x => x.Timestamp)
                            .Last();

                    var readdressedSubaddress = readdressedSubaddresses.FirstOrDefault(x => x.OldSubaddressId == nonDeletedSubaddressId);
                    if (readdressedSubaddress != null)
                    {
                        lastImportedCommand = allSubaddresses
                            .Where(x => x.SubaddressId == readdressedSubaddress.NewSubaddressId)
                            .OrderBy(x => x.Timestamp)
                            .Last();
                    }

                    if (latestCommands.All(x => x.SubaddressId != lastImportedCommand.SubaddressId))
                        latestCommands.Add(CreateCommandFromImportedCommand(lastImportedCommand));
                }

                var crabTerrainObjectId = latestCommands.First().TerrainObjectId;
                var crabTerrainObjectHouseNumberId = latestCommands.First().TerrainObjectHouseNumberId;

                var path = Path.Combine(FilesToProcessPath, $"{crabTerrainObjectId:D9}-{crabTerrainObjectHouseNumberId}.json");
                int fileNr = 2;

                while (File.Exists(path))
                {
                    path = Path.Combine(FilesToProcessPath, $"{crabTerrainObjectId:D9}-{crabTerrainObjectHouseNumberId}-{fileNr}.json");
                    fileNr++;
                }

                File.WriteAllText(
                    Path.Combine(path),
                    JsonConvert.SerializeObject(
                        new RegisterCrabImportRequest(JsonConvert.SerializeObject(
                            new FixGrar1359(latestCommands, new CrabTerrainObjectId(crabTerrainObjectId)),
                            commandsJsonSerializerSettings), typeof(FixGrar1359).FullName),
                        commandsJsonSerializerSettings));
            }
        }

        private static void SendCommands(List<RegisterCrabImportRequest[]> commandsToSend, JsonSerializerSettings commandsJsonSerializerSettings, ApplicationSettings appSettings)
        {
            var jsonToSend = JsonConvert.SerializeObject(commandsToSend, commandsJsonSerializerSettings);
            using (var client = CreateImportClient(appSettings))
            {
                var response = client
                    .PostAsync(
                        appSettings.EndpointUrl,
                        CreateJsonContent(jsonToSend))
                    .GetAwaiter()
                    .GetResult();

                response.EnsureSuccessStatusCode();
            }
        }

        private static ImportSubaddressFromCrab CreateCommandFromImportedCommand(AddressSubaddressWasImportedFromCrab lastImportedCommand)
        {
            return new ImportSubaddressFromCrab(
                new CrabTerrainObjectId(lastImportedCommand.TerrainObjectId),
                new CrabTerrainObjectHouseNumberId(lastImportedCommand.TerrainObjectHouseNumberId),
                new CrabSubaddressId(lastImportedCommand.SubaddressId),
                new CrabHouseNumberId(lastImportedCommand.HouseNumberId),
                new BoxNumber(lastImportedCommand.BoxNumber),
                new CrabBoxNumberType(lastImportedCommand.BoxNumberType),
                new CrabLifetime(lastImportedCommand.BeginDateTime, lastImportedCommand.EndDateTime),
                new CrabTimestamp(lastImportedCommand.Timestamp),
                new CrabOperator(lastImportedCommand.Operator),
                lastImportedCommand.Modification,
                lastImportedCommand.Organisation);
        }

        private static List<TerrainObjectHouseNumberWasImportedFromCrab> GetImportedTerrainObjectHouseNumbers(SqlConnection sqlConnection, int streamId, JsonSerializerSettings eventsJsonSerializerSettings)
        {
            return sqlConnection.Query<string>(
                    @"SELECT JsonData FROM [building-registry-events].BuildingRegistry.Messages
                            WHERE StreamIdInternal = @streamId AND [type] = 'TerrainObjectHouseNumberWasImportedFromCrab'
                            ORDER BY Position", new { streamId }, commandTimeout: 120)
                .Select(x => JsonConvert.DeserializeObject<TerrainObjectHouseNumberWasImportedFromCrab>(x, eventsJsonSerializerSettings))
                .ToList();
        }

        private static List<int> GetNextStreamsIds(string sqlConnectionString, int lastStreamId)
        {
            using (var sqlConnection = new SqlConnection(sqlConnectionString))
            {
                return sqlConnection.Query<int>(@"SELECT TOP(10000) IdInternal FROM [building-registry-events].[BuildingRegistry].[Streams] s
                    INNER JOIN [building-registry-events].[BuildingRegistry].[Messages] m on s.IdInternal = m.StreamIdInternal
                    WHERE IdInternal > @lastStreamId and m.[Type] = 'TerrainObjectHouseNumberWasImportedFromCrab'
                    GROUP BY IdInternal
                    ORDER BY IdInternal", new { lastStreamId }, commandTimeout: 120).ToList();
            }
        }

        private static List<AddressSubaddressWasImportedFromCrab> GetSubaddressesByStreamAndTerrainObjectHouseNumber(SqlConnection sqlConnection, int streamId, int terrainObjectHouseNumberId, JsonSerializerSettings eventsJsonSerializerSettings)
        {
            return sqlConnection.Query<string>(
                    @"SELECT JsonData FROM [building-registry-events].BuildingRegistry.Messages
                                        WHERE StreamIdInternal = @streamId AND [type] = 'AddressSubaddressWasImportedFromCrab' AND JSON_VALUE(JsonData, '$.terrainObjectHouseNumberId') = @terrainObjectHouseNumberId
                                        ORDER BY Position", new { streamId, terrainObjectHouseNumberId = terrainObjectHouseNumberId }, commandTimeout: 120)
                .Select(x => JsonConvert.DeserializeObject<AddressSubaddressWasImportedFromCrab>(x, eventsJsonSerializerSettings))
                .ToList();
        }

        private static List<HouseNumberWasReaddressedFromCrab> GetReaddressedHouseNumbersByStream(SqlConnection sqlConnection, int streamId, JsonSerializerSettings eventsJsonSerializerSettings)
        {
            return sqlConnection.Query<string>(
                    @"SELECT JsonData FROM [building-registry-events].BuildingRegistry.Messages
                            WHERE StreamIdInternal = @streamId AND [type] = 'HouseNumberWasReaddressedFromCrab'
                            ORDER BY Position", new { streamId }, commandTimeout: 120)
                .Select(x => JsonConvert.DeserializeObject<HouseNumberWasReaddressedFromCrab>(x, eventsJsonSerializerSettings))
                .ToList();
        }

        private static List<SubaddressWasReaddressedFromCrab> GetReaddressedSubaddressesByStream(
            SqlConnection sqlConnection,
            int streamId,
            TerrainObjectHouseNumberWasImportedFromCrab terrainObjectHouseNumber,
            JsonSerializerSettings eventsJsonSerializerSettings)
        {
            return sqlConnection.Query<string>(
                    @"SELECT JsonData FROM [building-registry-events].BuildingRegistry.Messages
                                        WHERE StreamIdInternal = @streamId AND [type] = 'SubaddressWasReaddressedFromCrab' AND JSON_VALUE(JsonData, '$.newTerrainObjectHouseNumberId') = @terrainObjectHouseNumberId
                                        ORDER BY Position", new { streamId, terrainObjectHouseNumberId = terrainObjectHouseNumber.TerrainObjectHouseNumberId }, commandTimeout: 120)
                .Select(x => JsonConvert.DeserializeObject<SubaddressWasReaddressedFromCrab>(x, eventsJsonSerializerSettings))
                .ToList();
        }

        protected static HttpClient CreateImportClient(ApplicationSettings settings)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(settings.EndpointBaseUrl),
                Timeout = TimeSpan.FromMinutes(settings.HttpTimeoutInMinutes)
            };

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

            return client;
        }

        protected static StringContent CreateJsonContent(string jsonValue)
            => new StringContent(jsonValue, Encoding.UTF8, MediaTypeNames.Application.Json);
    }
}
