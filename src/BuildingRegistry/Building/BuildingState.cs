namespace BuildingRegistry.Building
{
    using System.Collections.Generic;
    using Events;

    public partial class Building
    {
        public BuildingPersistentLocalId BuildingPersistentLocalId { get; private set; }
        public BuildingPersistentLocalIdAssignmentDate BuildingPersistentLocalIdAssignmentDate { get; private set; }
        public BuildingStatus BuildingStatus { get; private set; }
        public BuildingGeometry BuildingGeometry { get; private set; }
        public bool IsRemoved { get; private set; }
        public List<BuildingUnit> BuildingUnits { get; private set; } = new List<BuildingUnit>();

        private Building()
        {
            Register<BuildingWasMigrated>(When);
        }

        private void When(BuildingWasMigrated @event)
        {
            BuildingPersistentLocalId = new BuildingPersistentLocalId(@event.BuildingPersistentLocalId);
            BuildingPersistentLocalIdAssignmentDate = new BuildingPersistentLocalIdAssignmentDate(@event.BuildingPersistentLocalIdAssignmentDate);
            BuildingStatus = BuildingStatus.Parse(@event.BuildingStatus);
            BuildingGeometry = new BuildingGeometry(
                new ExtendedWkbGeometry(@event.ExtendedWkbGeometry),
                BuildingGeometryMethod.Parse(@event.GeometryMethod));
            IsRemoved = @event.IsRemoved;

            foreach (var buildingUnit in @event.BuildingUnits)
            {
                var newBuildingUnit = BuildingUnit.Migrate(
                    ApplyChange,
                    new BuildingUnitPersistentLocalId(buildingUnit.BuildingUnitPersistentLocalId),
                    BuildingUnitFunction.Parse(buildingUnit.Function),
                    BuildingUnitStatus.Parse(buildingUnit.Status),
                    buildingUnit.AddressPersistentLocalIds.ConvertAll(x => new AddressPersistentLocalId(x)),
                    new BuildingUnitPosition(
                        new ExtendedWkbGeometry(buildingUnit.ExtendedWkbGeometry),
                        BuildingUnitPositionGeometryMethod.Parse(buildingUnit.GeometryMethod)),
                    buildingUnit.IsRemoved);

                BuildingUnits.Add(newBuildingUnit);
            }
        }
    }
}
