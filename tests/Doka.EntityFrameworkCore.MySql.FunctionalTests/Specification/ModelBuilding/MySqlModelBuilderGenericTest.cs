namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.ModelBuilding;

/// <summary>
/// Executes every relational model-building contract through EF Core's strongly typed
/// builder surface.
/// </summary>
public sealed class MySqlModelBuilderGenericTest : MySqlModelBuilderTestBase
{
    public sealed class MySqlGenericNonRelationshipTest : MySqlNonRelationshipTestBase
    {
        public MySqlGenericNonRelationshipTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlGenericComplexTypeTest : MySqlComplexTypeTestBase
    {
        public MySqlGenericComplexTypeTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlGenericComplexCollectionTest : MySqlComplexCollectionTestBase
    {
        public MySqlGenericComplexCollectionTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlGenericInheritanceTest : MySqlInheritanceTestBase
    {
        public MySqlGenericInheritanceTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlGenericOneToManyTest : MySqlOneToManyTestBase
    {
        public MySqlGenericOneToManyTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlGenericManyToOneTest : MySqlManyToOneTestBase
    {
        public MySqlGenericManyToOneTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlGenericOneToOneTest : MySqlOneToOneTestBase
    {
        public MySqlGenericOneToOneTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlGenericManyToManyTest : MySqlManyToManyTestBase
    {
        public MySqlGenericManyToManyTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }

    public sealed class MySqlGenericOwnedTypesTest : MySqlOwnedTypesTestBase
    {
        public MySqlGenericOwnedTypesTest(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        protected override TestModelBuilder CreateModelBuilder(
            Action<ModelConfigurationBuilder>? configure = null
        ) => new GenericTestModelBuilder(Fixture, configure);
    }
}
