namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.ModelBuilding;

/// <summary>
/// Executes every relational model-building contract through EF Core's non-generic builder
/// surface.
/// </summary>
public sealed class MySqlModelBuilderNonGenericTest : MySqlModelBuilderTestBase
{
    public sealed class MySqlNonGenericNonRelationshipTest : MySqlNonRelationshipTestBase
    {
        public MySqlNonGenericNonRelationshipTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlNonGenericComplexTypeTest : MySqlComplexTypeTestBase
    {
        public MySqlNonGenericComplexTypeTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlNonGenericComplexCollectionTest : MySqlComplexCollectionTestBase
    {
        public MySqlNonGenericComplexCollectionTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlNonGenericInheritanceTest : MySqlInheritanceTestBase
    {
        public MySqlNonGenericInheritanceTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlNonGenericOneToManyTest : MySqlOneToManyTestBase
    {
        public MySqlNonGenericOneToManyTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlNonGenericManyToOneTest : MySqlManyToOneTestBase
    {
        public MySqlNonGenericManyToOneTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlNonGenericOneToOneTest : MySqlOneToOneTestBase
    {
        public MySqlNonGenericOneToOneTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlNonGenericManyToManyTest : MySqlManyToManyTestBase
    {
        public MySqlNonGenericManyToManyTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlNonGenericOwnedTypesTest : MySqlOwnedTypesTestBase
    {
        public MySqlNonGenericOwnedTypesTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new NonGenericTestModelBuilder(Fixture, configure);
    }
}
