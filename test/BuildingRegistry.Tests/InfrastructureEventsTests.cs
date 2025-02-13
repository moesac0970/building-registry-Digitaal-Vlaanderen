namespace Aiv.Vbr.Testing.Infrastructure.Events
{
    using Be.Vlaanderen.Basisregisters.AggregateSource;
    using Be.Vlaanderen.Basisregisters.EventHandling;
    using FluentAssertions;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using FluentAssertions.Equivalency;
    using Xunit;

    /// <summary>
    /// This class was generated using a nuget package called Aiv.Vbr.Testing.Infrastructure.Events.
    /// If you want to make significant changes to it, that would also make sense for other registries using these tests,
    /// consider updating the nuget package (repository: bitbucket.org:vlaamseoverheid/infrastructure-tests.git)
    /// </summary>
    public class InfrastructureEventsTests
    {
        private readonly IEnumerable<Type> _eventTypes;

        public InfrastructureEventsTests()
        {
            // Attempt to auto-discover the domain assembly using a type called "DomainAssemblyMarker".
            // If this class is not present, these tests will fail.
            var domainAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => GetAssemblyTypesSafe(a).Any(t => t?.Name == "DomainAssemblyMarker"));
            if (domainAssembly == null)
            {
                _eventTypes = Enumerable.Empty<Type>();
                return;
            }

            bool IsEventNamespace(Type t) => t.Namespace.EndsWith("Events");
            bool IsNotCompilerGenerated(MemberInfo t) => Attribute.GetCustomAttribute(t, typeof(CompilerGeneratedAttribute)) == null;

            _eventTypes = domainAssembly
                .GetTypes()
                .Where(t => t.IsClass && t.Namespace != null && IsEventNamespace(t) && IsNotCompilerGenerated(t));
        }

        [Fact(Skip = "BuildingWasMigrated.BuildingUnit is not an event.")]
        public void HasEventNameAttribute()
        {
            foreach (var type in _eventTypes)
                type
                    .GetCustomAttributes(typeof(EventNameAttribute), true)
                    .Should()
                    .NotBeEmpty($"Forgot EventName attribute on {type.FullName}");
        }

        [Fact(Skip = "BuildingWasMigrated.BuildingUnit is not an event.")]
        public void HasEventDescriptionAttributes()
        {
            foreach (var type in _eventTypes)
                type
                    .GetCustomAttributes(typeof(EventDescriptionAttribute), true)
                    .Should()
                    .NotBeEmpty($"Forgot EventDescription attribute on {type.FullName}");
        }

        [Fact]
        public void HasNoDuplicateEventNameAttributes()
        {
            var eventNames = new List<string>();

            foreach (var eventType in _eventTypes)
            {
                var newNames = eventType
                    .GetCustomAttributes(typeof(EventNameAttribute), true)
                    .OfType<EventNameAttribute>()
                    .Select(s => s.Value);

                foreach (var newName in newNames)
                {
                    eventNames.Contains(newName).Should().BeFalse($"Duplicate event name {newName}");
                    eventNames.Add(newName);
                }
            }
        }

        [Fact]
        public void HasNoValueObjectProperty()
        {
            foreach (var type in _eventTypes)
            {
                type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                    .SelectMany(s => GetParentTypes(s.PropertyType))
                    .Where(s => s.IsGenericType && typeof(ValueObject<>).IsAssignableFrom(s.GetGenericTypeDefinition()))
                    .Should()
                    .BeEmpty($"Value objects detected as property on {type.FullName}");
            }
        }

        [Fact]
        public void HasJsonConstructor()
        {
            foreach (var type in _eventTypes)
            {
                type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                    .SelectMany(s => s.GetCustomAttributes(typeof(JsonConstructorAttribute), true))
                    .Should()
                    .NotBeEmpty($"Forgot JsonConstructor on {type.FullName}");
            }
        }

        [Fact]
        public void HasNoPublicFields()
        {
            foreach (var type in _eventTypes)
            {
                type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Should()
                    .BeEmpty($"{type.FullName} has a public field");
            }
        }

        [Fact]
        public void HasJsonConstructorWithCorrectParameters()
        {
            foreach (var type in _eventTypes)
            {
                var propertyNamesCamelCased =
                    type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Select(x => x.Name)
                        .Select(x => x[0].ToString().ToLowerInvariant() + x.Substring(1))
                        .ToList();

                type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(s => s.GetCustomAttributes(typeof(JsonConstructorAttribute), true).Any())
                    .SelectMany(x => x.GetParameters().Select(x => x.Name))
                    .Should().BeEquivalentTo(propertyNamesCamelCased, $"JsonConstructor without correct parameters on {type.FullName}");
            }
        }

        public static IEnumerable<Type> GetParentTypes(Type type)
        {
            // is there any base type?
            if (type == null || type.BaseType == null)
                yield break;

            // return all implemented or inherited interfaces
            foreach (var i in type.GetInterfaces())
                yield return i;

            // return all inherited types
            var currentBaseType = type.BaseType;
            while (currentBaseType != null)
            {
                yield return currentBaseType;
                currentBaseType = currentBaseType.BaseType;
            }
        }

        public static IEnumerable<Type> GetAssemblyTypesSafe(Assembly assembly)
        {
            List<Type> types;

            try
            {
                types = assembly.GetTypes().ToList();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToList();
            }

            return types;
        }
    }
}
