﻿namespace BuildingRegistry.Projections.LastChangedList.Migrations
{
    using System;
    using Be.Vlaanderen.Basisregisters.ProjectionHandling.LastChangedList;
    using Microsoft.EntityFrameworkCore.Migrations;

    public partial class Initial_MigrateOslo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@$"
INSERT INTO [{LastChangedListContext.Schema}].[{LastChangedListContext.TableName}] (Id, CacheKey, Uri, AcceptType, Position, LastPopulatedPosition, ErrorCount, LastError, LastErrorMessage)
SELECT
  CONCAT(SUBSTRING(Id, 0, LEN(Id) - 4), '.jsonld'),
  CONCAT(
	'oslo/buildingunit:',
	SUBSTRING(CacheKey, LEN(LEFT(CacheKey, CHARINDEX (':', CacheKey))) + 1, LEN(CacheKey) - LEN(LEFT(CacheKey,
		CHARINDEX (':', CacheKey))) - LEN(RIGHT(CacheKey, LEN(CacheKey) - CHARINDEX ('.', CacheKey))) - 1),
	'.jsonld'),
  REPLACE(Uri, '/v1/', '/v2/'),
  'application/ld+json',
  Position,
  0,
  0,
  null,
  null
FROM [{LastChangedListContext.Schema}].[{LastChangedListContext.TableName}]
WHERE Id like '%.json' AND CacheKey like 'legacy/buildingunit:%'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
