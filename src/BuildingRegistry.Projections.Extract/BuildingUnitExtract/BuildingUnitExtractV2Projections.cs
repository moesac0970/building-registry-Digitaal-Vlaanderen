namespace BuildingRegistry.Projections.Extract.BuildingUnitExtract
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Be.Vlaanderen.Basisregisters.GrAr.Common;
    using Be.Vlaanderen.Basisregisters.GrAr.Extracts;
    using Be.Vlaanderen.Basisregisters.ProjectionHandling.Connector;
    using Be.Vlaanderen.Basisregisters.ProjectionHandling.SqlStreamStore;
    using Be.Vlaanderen.Basisregisters.Shaperon;
    using Be.Vlaanderen.Basisregisters.Utilities.HexByteConvertor;
    using Building;
    using Building.Events;
    using Microsoft.Extensions.Options;
    using NetTopologySuite.Geometries;
    using NetTopologySuite.IO;
    using NodaTime;
    using Point = Be.Vlaanderen.Basisregisters.Shaperon.Point;

    [ConnectedProjectionName("Extract gebouweenheden")]
    [ConnectedProjectionDescription("Projectie die de gebouweenheden data voor het gebouweenheden extract voorziet.")]
    public class BuildingUnitExtractV2Projections : ConnectedProjection<ExtractContext>
    {
        private const string NotRealized = "NietGerealiseerd";
        private const string Planned = "Gepland";
        private const string Realized = "Gerealiseerd";
        private const string Retired = "Gehistoreerd";

        private const string Unknown = "NietGekend";
        private const string Common = "GemeenschappelijkDeel";

        private const string DerivedFromObject = "AfgeleidVanObject";
        private const string AppointedByAdministrator = "AangeduidDoorBeheerder";

        private readonly Encoding _encoding;

        public BuildingUnitExtractV2Projections(IOptions<ExtractConfig> extractConfig, Encoding encoding, WKBReader wkbReader)
        {
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));

            When<Envelope<BuildingWasMigrated>>(async (context, message, ct) =>
            {
                var buildingUnitBuildingItemV2 = new BuildingUnitBuildingItemV2
                {
                    BuildingPersistentLocalId = message.Message.BuildingPersistentLocalId,
                    IsRemoved = message.Message.IsRemoved,
                };

                var buildingStatus = BuildingStatus.Parse(message.Message.BuildingStatus);
                if (buildingStatus == BuildingStatus.Retired || buildingStatus == BuildingStatus.NotRealized)
                {
                    buildingUnitBuildingItemV2.BuildingRetiredStatus = buildingStatus;
                }

                await context.BuildingUnitBuildingsV2.AddAsync(buildingUnitBuildingItemV2, ct);

                if (message.Message.IsRemoved)
                {
                    return;
                }

                foreach (var buildingUnit in message.Message.BuildingUnits)
                {
                    if (buildingUnit.IsRemoved)
                    {
                        continue;
                    }

                    var buildingUnitItemV2 = new BuildingUnitExtractItemV2
                    {
                        BuildingPersistentLocalId = message.Message.BuildingPersistentLocalId,
                        BuildingUnitPersistentLocalId = buildingUnit.BuildingUnitPersistentLocalId,
                        DbaseRecord = new BuildingUnitDbaseRecord
                        {
                            id = { Value = $"{extractConfig.Value.DataVlaanderenNamespaceBuildingUnit}/{buildingUnit.BuildingUnitPersistentLocalId}" },
                            gebouwehid = { Value = buildingUnit.BuildingUnitPersistentLocalId },
                            gebouwid = { Value = message.Message.BuildingPersistentLocalId.ToString() },
                            functie = { Value = MapFunction(BuildingUnitFunction.Parse(buildingUnit.Function)) },
                            status = { Value = MapStatus(BuildingUnitStatus.Parse(buildingUnit.Status)) },
                            posgeommet = { Value = MapGeometryMethod(BuildingUnitPositionGeometryMethod.Parse(buildingUnit.GeometryMethod)) },
                            versieid = { Value = message.Message.Provenance.Timestamp.ToBelgianDateTimeOffset().FromDateTimeOffset() }
                        }.ToBytes(_encoding)
                    };

                    var geometry = wkbReader.Read(buildingUnit.ExtendedWkbGeometry.ToByteArray());
                    UpdateGeometry(buildingUnitItemV2, geometry);

                    await context.BuildingUnitExtractV2.AddAsync(buildingUnitItemV2, ct);
                }
            });
        }

        private static string MapFunction(BuildingUnitFunction function)
        {
            var dictionary = new Dictionary<BuildingUnitFunction, string>
            {
                { BuildingUnitFunction.Common, Common },
                { BuildingUnitFunction.Unknown, Unknown }
            };

            return dictionary[function];
        }

        private static string MapStatus(BuildingUnitStatus status)
        {
            var dictionary = new Dictionary<BuildingUnitStatus, string>
            {
                { BuildingUnitStatus.Planned, Planned },
                { BuildingUnitStatus.Retired, Retired },
                { BuildingUnitStatus.NotRealized, NotRealized },
                { BuildingUnitStatus.Realized, Realized }
            };

            return dictionary[status];
        }

        private static string MapGeometryMethod(BuildingUnitPositionGeometryMethod geometryMethod)
        {
            var dictionary = new Dictionary<BuildingUnitPositionGeometryMethod, string>
            {
                { BuildingUnitPositionGeometryMethod.AppointedByAdministrator, AppointedByAdministrator },
                { BuildingUnitPositionGeometryMethod.DerivedFromObject, DerivedFromObject }
            };

            return dictionary[geometryMethod];
        }

        private static IEnumerable<BuildingUnitExtractItemV2> GetBuildingUnitsByBuilding(ExtractContext context, int buildingPersistentLocalId)
        {
            var units = context.BuildingUnitExtractV2.Local.Where(x => x.BuildingPersistentLocalId == buildingPersistentLocalId)
                .Union(context.BuildingUnitExtractV2.Where(x => x.BuildingPersistentLocalId == buildingPersistentLocalId).ToList());
            return units;
        }

        private static void UpdateGeometry(BuildingUnitExtractItemV2 item, Geometry? geometry)
        {
            if (geometry == null)
            {
                item.ShapeRecordContentLength = 0;
                item.ShapeRecordContent = null;
                item.MinimumY = 0;
                item.MinimumX = 0;
                item.MaximumY = 0;
                item.MaximumX = 0;
            }
            else
            {
                var pointShapeContent = new PointShapeContent(new Point(geometry.Coordinate.X, geometry.Coordinate.Y));
                item.ShapeRecordContent = pointShapeContent.ToBytes();
                item.ShapeRecordContentLength = pointShapeContent.ContentLength.ToInt32();
                item.MinimumX = pointShapeContent.Shape.X;
                item.MaximumX = pointShapeContent.Shape.X;
                item.MinimumY = pointShapeContent.Shape.Y;
                item.MaximumY = pointShapeContent.Shape.Y;
            }
        }

        private void UpdateStatus(BuildingUnitExtractItemV2 buildingUnit, string status)
            => UpdateRecord(buildingUnit, record => record.status.Value = status);

        private void UpdateGeometryMethod(BuildingUnitExtractItemV2 buildingUnit, string method)
            => UpdateRecord(buildingUnit, record => record.posgeommet.Value = method);

        private void UpdateVersie(BuildingUnitExtractItemV2 buildingUnit, Instant timestamp)
            => UpdateRecord(buildingUnit, record => record.versieid.SetValue(timestamp.ToBelgianDateTimeOffset()));

        private void UpdateRecord(BuildingUnitExtractItemV2 buildingUnit, Action<BuildingUnitDbaseRecord> updateFunc)
        {
            var record = new BuildingUnitDbaseRecord();
            record.FromBytes(buildingUnit.DbaseRecord, _encoding);

            updateFunc(record);

            buildingUnit.DbaseRecord = record.ToBytes(_encoding);
        }
    }
}
