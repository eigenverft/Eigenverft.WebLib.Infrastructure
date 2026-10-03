using System;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.WebLib.SerilogRelayReceiver.Tests
{
    [TestClass]
    public sealed class ReleaseContractTests
    {
        [TestMethod]
        public void PublicTypeSurfaceMatchesOnePointZeroContract()
        {
            Assembly assembly = typeof(SerilogRelayBatch).Assembly;

            string[] actual = assembly
                .GetExportedTypes()
                .Select(type => type.FullName!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            string[] expected =
            {
                "Eigenverft.WebLib.SerilogRelayReceiver.ISerilogRelayBatchHandler",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayBatch",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayEvent",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayReceivedEvent",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayReceiverEndpointRouteBuilderExtensions",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayReceiverEntityFrameworkCoreEndpointRouteBuilderExtensions",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayReceiverEntityFrameworkCoreServiceCollectionExtensions",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayReceiverModelBuilderExtensions",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayReceiverOptions",
                "Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayReceiverServiceCollectionExtensions",
            };

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void DataContractsMatchOnePointZeroMemberShape()
        {
            AssertPublicProperties(
                typeof(SerilogRelayBatch),
                "BatchId:System.String",
                "Count:System.Int32",
                "Logs:System.Collections.Generic.List`1[Eigenverft.WebLib.SerilogRelayReceiver.SerilogRelayEvent]",
                "ProtocolVersion:System.Int32",
                "Timestamp:System.String");

            AssertPublicProperties(
                typeof(SerilogRelayEvent),
                "ApplicationId:System.String",
                "ApplicationVersion:System.String",
                "EventId:System.String",
                "Exception:System.String",
                "Id:System.Int64",
                "Level:System.String",
                "MachineId:System.String",
                "MessageTemplate:System.String",
                "ProcessId:System.Int32",
                "Properties:System.String",
                "RenderMessage:System.String",
                "SpanId:System.String",
                "Timestamp:System.String",
                "TraceId:System.String");

            AssertPublicProperties(
                typeof(SerilogRelayReceiverOptions),
                "BearerToken:System.String",
                "MaximumBatchEvents:System.Int32");

            AssertPublicProperties(
                typeof(SerilogRelayReceivedEvent),
                "ApplicationId:System.String",
                "ApplicationVersion:System.String",
                "BatchCount:System.Int32",
                "BatchId:System.String",
                "BatchTimestamp:System.String",
                "EventId:System.String",
                "Exception:System.String",
                "Level:System.String",
                "MachineId:System.String",
                "MessageTemplate:System.String",
                "ProcessId:System.Int32",
                "Properties:System.String",
                "ProtocolVersion:System.Int32",
                "ReceivedAtUtc:System.DateTimeOffset",
                "ReceiveId:System.Int64",
                "RenderMessage:System.String",
                "SenderLocalId:System.Int64",
                "SpanId:System.String",
                "Timestamp:System.String",
                "TraceId:System.String");
        }

        [TestMethod]
        public void PublicEntryPointSignaturesMatchOnePointZeroContract()
        {
            AssertGenericExtension(
                typeof(SerilogRelayReceiverServiceCollectionExtensions),
                "AddSerilogRelayReceiver",
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
                new[]
                {
                    typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
                },
                typeof(ISerilogRelayBatchHandler));

            AssertGenericExtension(
                typeof(SerilogRelayReceiverEntityFrameworkCoreServiceCollectionExtensions),
                "AddSerilogRelayReceiverEntityFrameworkCore",
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
                new[]
                {
                    typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
                },
                typeof(DbContext));

            AssertGenericExtension(
                typeof(SerilogRelayReceiverEndpointRouteBuilderExtensions),
                "MapSerilogRelayReceiver",
                typeof(Microsoft.AspNetCore.Builder.IEndpointConventionBuilder),
                new[]
                {
                    typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder),
                    typeof(string),
                    typeof(Action<SerilogRelayReceiverOptions>),
                },
                typeof(ISerilogRelayBatchHandler));

            AssertGenericExtension(
                typeof(SerilogRelayReceiverEntityFrameworkCoreEndpointRouteBuilderExtensions),
                "MapSerilogRelayReceiverEntityFrameworkCore",
                typeof(Microsoft.AspNetCore.Builder.IEndpointConventionBuilder),
                new[]
                {
                    typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder),
                    typeof(string),
                    typeof(Action<SerilogRelayReceiverOptions>),
                },
                typeof(DbContext));

            MethodInfo configure = typeof(SerilogRelayReceiverModelBuilderExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Single();
            Assert.AreEqual("ConfigureSerilogRelayReceiver", configure.Name);
            Assert.AreEqual(typeof(ModelBuilder), configure.ReturnType);
            CollectionAssert.AreEqual(
                new[] { typeof(ModelBuilder) },
                configure.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

            MethodInfo handle = typeof(ISerilogRelayBatchHandler)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Single();
            Assert.AreEqual("HandleAsync", handle.Name);
            Assert.AreEqual(typeof(System.Threading.Tasks.ValueTask), handle.ReturnType);
            CollectionAssert.AreEqual(
                new[]
                {
                    typeof(SerilogRelayBatch),
                    typeof(System.Threading.CancellationToken),
                },
                handle.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        }

        [TestMethod]
        public void BuiltInPersistenceAssemblyStaysDatabaseProviderNeutral()
        {
            AssemblyName[] references = typeof(SerilogRelayReceivedEvent)
                .Assembly
                .GetReferencedAssemblies();

            Assert.IsTrue(
                references.Any(reference =>
                    reference.Name == "Microsoft.EntityFrameworkCore"));

            string[] forbiddenPrefixes =
            {
                "Microsoft.EntityFrameworkCore.Relational",
                "Microsoft.EntityFrameworkCore.Sqlite",
                "Microsoft.EntityFrameworkCore.SqlServer",
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Pomelo.EntityFrameworkCore.MySql",
            };

            foreach (AssemblyName reference in references)
            {
                Assert.IsFalse(
                    forbiddenPrefixes.Any(prefix =>
                        reference.Name?.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase) == true),
                    $"Product assembly must remain provider-neutral but references '{reference.Name}'.");
            }
        }

        [TestMethod]
        public void EfCoreModelMatchesOnePointZeroSchemaContract()
        {
            using var database = CreateDatabase();

            IEntityType entity =
                database.Model.FindEntityType(typeof(SerilogRelayReceivedEvent))
                ?? throw new AssertFailedException(
                    "SerilogRelayReceivedEvent is missing from the EF Core model.");

            Assert.AreEqual("SerilogRelayReceivedEvents", entity.GetTableName());

            IKey primaryKey =
                entity.FindPrimaryKey()
                ?? throw new AssertFailedException("Receiver entity primary key is missing.");
            CollectionAssert.AreEqual(
                new[] { nameof(SerilogRelayReceivedEvent.ReceiveId) },
                primaryKey.Properties.Select(property => property.Name).ToArray());

            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.BatchId), false, 36);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.BatchTimestamp), true, 64);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.EventId), false, 36);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.ApplicationId), false, 255);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.ApplicationVersion), true, 255);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.MachineId), true, 256);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.Timestamp), true, 64);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.Level), true, 32);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.RenderMessage), true, null);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.MessageTemplate), true, null);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.TraceId), true, 64);
            AssertProperty(entity, nameof(SerilogRelayReceivedEvent.SpanId), true, 32);

            string[] indexes = entity
                .GetIndexes()
                .Select(index =>
                    $"{string.Join(",", index.Properties.Select(property => property.Name))}|Unique={index.IsUnique}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    "ApplicationId,ReceivedAtUtc|Unique=False",
                    "BatchId|Unique=False",
                    "EventId|Unique=False",
                    "ReceivedAtUtc|Unique=False",
                },
                indexes);
        }

        [TestMethod]
        public void HostRelationalProviderCanGenerateOnePointZeroSchema()
        {
            using var database = CreateDatabase();

            string script = database.Database.GenerateCreateScript();

            StringAssert.Contains(script, "SerilogRelayReceivedEvents");
            StringAssert.Contains(script, "IX_SerilogRelayReceivedEvents_BatchId");
            StringAssert.Contains(script, "IX_SerilogRelayReceivedEvents_EventId");
            StringAssert.Contains(script, "IX_SerilogRelayReceivedEvents_ReceivedAtUtc");
            StringAssert.Contains(
                script,
                "IX_SerilogRelayReceivedEvents_ApplicationId_ReceivedAtUtc");
        }

        private static ReleaseContractDbContext CreateDatabase()
        {
            var options = new DbContextOptionsBuilder<ReleaseContractDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;

            return new ReleaseContractDbContext(options);
        }

        private static void AssertPublicProperties(
            Type type,
            params string[] expected)
        {
            string[] actual = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(property => $"{property.Name}:{property.PropertyType}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            Array.Sort(expected, StringComparer.Ordinal);
            CollectionAssert.AreEqual(expected, actual);
        }

        private static void AssertGenericExtension(
            Type extensionType,
            string expectedName,
            Type expectedReturnType,
            Type[] expectedParameterTypes,
            Type expectedGenericConstraint)
        {
            MethodInfo method = extensionType
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Single();

            Assert.AreEqual(expectedName, method.Name);
            Assert.AreEqual(expectedReturnType, method.ReturnType);
            Assert.AreEqual(1, method.GetGenericArguments().Length);

            CollectionAssert.AreEqual(
                expectedParameterTypes,
                method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

            Type genericArgument = method.GetGenericArguments()[0];
            Type[] constraints = genericArgument.GetGenericParameterConstraints();
            Assert.IsTrue(
                constraints.Contains(expectedGenericConstraint),
                $"Generic parameter on '{expectedName}' must retain constraint '{expectedGenericConstraint}'.");
        }

        private static void AssertProperty(
            IEntityType entity,
            string propertyName,
            bool nullable,
            int? maximumLength)
        {
            IProperty property =
                entity.FindProperty(propertyName)
                ?? throw new AssertFailedException(
                    $"Expected EF Core property '{propertyName}' was not found.");

            Assert.AreEqual(nullable, property.IsNullable, propertyName);
            Assert.AreEqual(maximumLength, property.GetMaxLength(), propertyName);
        }

        private sealed class ReleaseContractDbContext : DbContext
        {
            internal ReleaseContractDbContext(
                DbContextOptions<ReleaseContractDbContext> options)
                : base(options)
            {
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.ConfigureSerilogRelayReceiver();
            }
        }
    }
}
