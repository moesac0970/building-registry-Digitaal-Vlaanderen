namespace BuildingRegistry.Building
{
    using System;

    using Be.Vlaanderen.Basisregisters.AggregateSource;
    using Be.Vlaanderen.Basisregisters.GrAr.Provenance;

    public class ProvenanceFactory<TAggregateRoot> : IProvenanceFactory<TAggregateRoot>
        where TAggregateRoot : IAggregateRootEntity
    {
        public bool CanCreateFrom<TCommand>() => typeof(IHasProvenance).IsAssignableFrom(typeof(TCommand));
        public Provenance CreateFrom(object provenanceHolder, TAggregateRoot aggregate)
        {
            if (provenanceHolder is not IHasCommandProvenance provenance)
                throw new ApplicationException($"Cannot create provenance from {provenanceHolder.GetType().Name}");

            return provenance.Provenance;
        }
    }
}
