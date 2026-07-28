using Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;
using Microsoft.EntityFrameworkCore.ModelBuilding;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.ModelBuilding;

/// <summary>
/// Binds every relational model-building contract family to the provider's convention set.
/// The generic and non-generic suites supply the concrete builder API used by each test.
/// </summary>
public abstract class MySqlModelBuilderTestBase : RelationalModelBuilderTest
{
    [Trait("Category", "Spec")]
    public abstract class MySqlNonRelationshipTestBase : RelationalNonRelationshipTestBase,
        IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlNonRelationshipTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }
    }

    [Trait("Category", "Spec")]
    public abstract class MySqlComplexTypeTestBase : RelationalComplexTypeTestBase,
        IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlComplexTypeTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_add_shadow_primitive_collections_when_they_have_been_ignored() =>
            base.Can_add_shadow_primitive_collections_when_they_have_been_ignored();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_add_shadow_properties_when_they_have_been_ignored() =>
            base.Can_add_shadow_properties_when_they_have_been_ignored();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_custom_value_generator_for_primitive_collections() =>
            base.Can_set_custom_value_generator_for_primitive_collections();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_custom_value_generator_for_properties() =>
            base.Can_set_custom_value_generator_for_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_max_length_for_primitive_collections() =>
            base.Can_set_max_length_for_primitive_collections();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_max_length_for_properties() => base.Can_set_max_length_for_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_max_length_for_property_type() => base.Can_set_max_length_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_precision_and_scale_for_properties() =>
            base.Can_set_precision_and_scale_for_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_precision_and_scale_for_property_type() =>
            base.Can_set_precision_and_scale_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_primitive_collection_annotation_when_no_clr_property() =>
            base.Can_set_primitive_collection_annotation_when_no_clr_property();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_sentinel_for_primitive_collections() =>
            base.Can_set_sentinel_for_primitive_collections();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_sentinel_for_properties() => base.Can_set_sentinel_for_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_sentinel_for_property_type() => base.Can_set_sentinel_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_unbounded_max_length_for_property_type() =>
            base.Can_set_unbounded_max_length_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_unicode_for_primitive_collections() =>
            base.Can_set_unicode_for_primitive_collections();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_unicode_for_properties() => base.Can_set_unicode_for_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_unicode_for_property_type() => base.Can_set_unicode_for_property_type();

        [Fact]
        public override void Can_specify_discriminator_value() => base.Can_specify_discriminator_value();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_specify_discriminator_without_explicit_value() =>
            base.Can_specify_discriminator_without_explicit_value();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Non_nullable_properties_cannot_be_made_optional() =>
            base.Non_nullable_properties_cannot_be_made_optional();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Primitive_collections_are_required_by_default_only_if_CLR_type_is_nullable() =>
            base.Primitive_collections_are_required_by_default_only_if_CLR_type_is_nullable();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Primitive_collections_can_be_made_concurrency_tokens() =>
            base.Primitive_collections_can_be_made_concurrency_tokens();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Primitive_collections_can_be_made_optional() =>
            base.Primitive_collections_can_be_made_optional();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Primitive_collections_can_be_made_required() =>
            base.Primitive_collections_can_be_made_required();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Primitive_collections_can_be_set_to_generate_values_on_Add() =>
            base.Primitive_collections_can_be_set_to_generate_values_on_Add();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void
            Primitive_collections_specified_by_string_are_shadow_properties_unless_already_known_to_be_CLR_properties() =>
            base
                .Primitive_collections_specified_by_string_are_shadow_properties_unless_already_known_to_be_CLR_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_are_required_by_default_only_if_CLR_type_is_nullable() =>
            base.Properties_are_required_by_default_only_if_CLR_type_is_nullable();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_be_made_concurrency_tokens() =>
            base.Properties_can_be_made_concurrency_tokens();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_be_made_optional() => base.Properties_can_be_made_optional();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_be_made_required() => base.Properties_can_be_made_required();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_be_set_to_generate_values_on_Add() =>
            base.Properties_can_be_set_to_generate_values_on_Add();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_have_access_mode_set() => base.Properties_can_have_access_mode_set();

        // These probes use distinct names because xUnit rejects reactivating the
        // official methods while their same-named generic helper overloads exist.
        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public void Issue_35613_custom_type_value_converter_probe() =>
            base.Properties_can_have_custom_type_value_converter_type_set();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public void Issue_35613_non_generic_value_converter_probe() =>
            base.Properties_can_have_non_generic_value_converter_set();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public void Issue_35613_provider_type_probe() => base.Properties_can_have_provider_type_set();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_have_provider_type_set_for_type() =>
            base.Properties_can_have_provider_type_set_for_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_have_value_converter_set() =>
            base.Properties_can_have_value_converter_set();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_have_value_converter_set_inline() =>
            base.Properties_can_have_value_converter_set_inline();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_set_row_version() => base.Properties_can_set_row_version();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void
            Properties_specified_by_string_are_shadow_properties_unless_already_known_to_be_CLR_properties() => base
            .Properties_specified_by_string_are_shadow_properties_unless_already_known_to_be_CLR_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Value_converter_configured_on_non_nullable_type_is_applied() =>
            base.Value_converter_configured_on_non_nullable_type_is_applied();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Value_converter_configured_on_nullable_type_overrides_non_nullable() =>
            base.Value_converter_configured_on_nullable_type_overrides_non_nullable();
    }

    [Trait("Category", "Spec")]
    public abstract class MySqlComplexCollectionTestBase : RelationalComplexCollectionTestBase,
        IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlComplexCollectionTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_add_shadow_properties_when_they_have_been_ignored() =>
            base.Can_add_shadow_properties_when_they_have_been_ignored();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_map_a_tuple_collection() => base.Can_map_a_tuple_collection();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_custom_value_generator_for_properties() =>
            base.Can_set_custom_value_generator_for_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_max_length_for_property_type() => base.Can_set_max_length_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_precision_and_scale_for_property_type() =>
            base.Can_set_precision_and_scale_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_sentinel_for_properties() => base.Can_set_sentinel_for_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_sentinel_for_property_type() => base.Can_set_sentinel_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_unbounded_max_length_for_property_type() =>
            base.Can_set_unbounded_max_length_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_unicode_for_properties() => base.Can_set_unicode_for_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Can_set_unicode_for_property_type() => base.Can_set_unicode_for_property_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Non_nullable_properties_cannot_be_made_optional() =>
            base.Non_nullable_properties_cannot_be_made_optional();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_are_required_by_default_only_if_CLR_type_is_nullable() =>
            base.Properties_are_required_by_default_only_if_CLR_type_is_nullable();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_be_made_optional() => base.Properties_can_be_made_optional();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_be_made_required() => base.Properties_can_be_made_required();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_have_access_mode_set() => base.Properties_can_have_access_mode_set();

        // These probes use distinct names because xUnit rejects reactivating the
        // official methods while their same-named generic helper overloads exist.
        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public void Issue_35613_custom_type_value_converter_probe() =>
            base.Properties_can_have_custom_type_value_converter_type_set();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public void Issue_35613_non_generic_value_converter_probe() =>
            base.Properties_can_have_non_generic_value_converter_set();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public void Issue_35613_provider_type_probe() => base.Properties_can_have_provider_type_set();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_have_provider_type_set_for_type() =>
            base.Properties_can_have_provider_type_set_for_type();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_have_value_converter_set() =>
            base.Properties_can_have_value_converter_set();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Properties_can_have_value_converter_set_inline() =>
            base.Properties_can_have_value_converter_set_inline();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void
            Properties_specified_by_string_are_shadow_properties_unless_already_known_to_be_CLR_properties() => base
            .Properties_specified_by_string_are_shadow_properties_unless_already_known_to_be_CLR_properties();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Value_converter_configured_on_non_nullable_type_is_applied() =>
            base.Value_converter_configured_on_non_nullable_type_is_applied();

        [SpecFrameworkLimitationFact("EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES")]
        public override void Value_converter_configured_on_nullable_type_overrides_non_nullable() =>
            base.Value_converter_configured_on_nullable_type_overrides_non_nullable();
    }

    [Trait("Category", "Spec")]
    public abstract class MySqlInheritanceTestBase : RelationalInheritanceTestBase,
        IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlInheritanceTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }
    }

    [Trait("Category", "Spec")]
    public abstract class MySqlOneToManyTestBase : RelationalOneToManyTestBase, IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlOneToManyTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }
    }

    [Trait("Category", "Spec")]
    public abstract class MySqlManyToOneTestBase : RelationalManyToOneTestBase, IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlManyToOneTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }
    }

    [Trait("Category", "Spec")]
    public abstract class MySqlOneToOneTestBase : RelationalOneToOneTestBase, IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlOneToOneTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }
    }

    [Trait("Category", "Spec")]
    public abstract class MySqlManyToManyTestBase : RelationalManyToManyTestBase,
        IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlManyToManyTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }
    }

    [Trait("Category", "Spec")]
    public abstract class MySqlOwnedTypesTestBase : RelationalOwnedTypesTestBase,
        IClassFixture<MySqlModelBuilderFixture>
    {
        protected MySqlOwnedTypesTestBase(
            MySqlModelBuilderFixture fixture
        ) : base(fixture) { }
    }

    /// <summary>
    /// Supplies the provider conventions and services shared by all model-building suites.
    /// </summary>
    public sealed class MySqlModelBuilderFixture : RelationalModelBuilderFixture
    {
        public override TestHelpers TestHelpers => MySqlTestHelpers.Instance;
    }
}
