using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexByteBudgetTests
{
    private static void ConfigureFacetEntity(
        ModelBuilder modelBuilder,
        EntityTypeBuilder<Dictionary<string, object>> entity,
        string scenario
    )
    {
        entity.Property<int>("Id");
        entity.HasKey("Id");
        var first = entity.Property<string>("A").IsRequired();
        entity.Property<string>("B").IsRequired();

        switch (scenario)
        {
            case "charset-eucjpms":
            case "charset-ujis":
            case "charset-euckr":
                modelBuilder.HasCharSet(scenario[8..]);
                first.HasMaxLength(1000);
                break;
            case "float-metadata":
                entity.Ignore("A");
                entity.Ignore("B");
                entity.Property<byte[]>("A").HasMaxLength(3065);
                entity.Property<float>("B").HasColumnType("float");
                break;
            case "decimal-width-seven":
                entity.Ignore("A");
                entity.Ignore("B");
                entity.Property<decimal>("D").HasColumnType("decimal(7,0)");
                entity.HasIndex("D");

                return;

            case "decimal-boundary-pass":
            case "decimal-boundary-fail":
            case "decimal-explicit-overflow":
                first.HasMaxLength(255);
                entity.Property<string>("B").HasMaxLength(255);
                entity.Property<string>("C").HasMaxLength(
                    scenario == "decimal-boundary-pass" ? 254 : 255);
                entity.Property<decimal>("D");
                var finalPart = entity.Property<string>("E").IsRequired();

                if (scenario == "decimal-explicit-overflow")
                {
                    finalPart.HasMaxLength(1);
                }

                entity.HasIndex("A", "B", "C", "D", "E");

                return;

            case "text-prefix":
            case "text-prefix-overlong":
                first.HasColumnType("longtext");
                entity.HasIndex("A", "B").HasPrefixLength(scenario == "text-prefix" ? 700 : 769, 0);

                return;

            case "blob-prefix":
                entity.Ignore("A");
                entity.Property<byte[]>("A").HasColumnType("longblob");
                entity.HasIndex("A", "B").HasPrefixLength(700, 0);

                return;

            case "table-collation":
            case "legacy-table-collation":
                modelBuilder.Model.RemoveAnnotation(MySqlAnnotationNames.CharSet);
                modelBuilder.UseCollation("latin1_bin");
                entity.HasAnnotation(
                    scenario == "table-collation"
                        ? RelationalAnnotationNames.Collation
                        : MySqlAnnotationNames.Collation,
                    "utf8mb4_bin");
                first.HasMaxLength(700);
                break;
            case "inline-charset":
            case "inline-collation":
            case "binary-modifier":
                modelBuilder.HasCharSet("latin1");
                first.HasColumnType(scenario switch
                {
                    "inline-charset" => "varchar(750) CHARACTER SET utf8mb4",
                    "inline-collation" => "varchar(750) COLLATE utf8mb4_bin",
                    _ => "varchar(750) BINARY",
                });
                break;
            case "explicit-length":
                first.HasMaxLength(700);
                break;
            case "explicit-store":
                first.HasColumnType("varchar(700)");
                break;
            case "latin1":
                modelBuilder.HasCharSet("latin1");
                first.HasMaxLength(700);
                break;
            case "property-collation":
                modelBuilder.HasCharSet("latin1");
                first.HasMaxLength(700).UseCollation("utf8mb4_bin");
                entity.Property<string>("B").UseCollation("utf8mb4_bin");
                break;
            case "unknown-charset":
                modelBuilder.HasCharSet("custom_charset");
                first.HasMaxLength(700);
                break;
            case "enum":
            case "set":
                first.HasColumnType(scenario + "('a','b')");
                entity.Property<byte[]>("C").HasMaxLength(2816);
                entity.HasIndex("A", "B", "C");

                return;

            case "converter-hint":
                first.HasMaxLength(700);
                entity.Ignore("B");
                entity.Property<BudgetCode>("B").HasConversion(
                    new ValueConverter<BudgetCode, string>(
                        value => value.Value,
                        value => new BudgetCode(value),
                        new ConverterMappingHints(size: 32)));
                break;
            case "converter":
                first.HasMaxLength(700);
                entity.Ignore("B");
                entity.Property<BudgetCode>("B")
                    .HasConversion(value => value.Value, value => new BudgetCode(value));
                break;
            case "binary-boundary":
                entity.Ignore("A");
                entity.Ignore("B");
                entity.Property<byte[]>("A").HasMaxLength(3056);
                entity.Property<byte[]>("B");
                break;
            case "guid-binary":
            case "guid-char":
            case "guid-uuid":
                first.HasMaxLength(600);
                entity.Property<Guid>("G").HasMySqlGuidFormat(scenario switch
                {
                    "guid-char" => MySqlGuidFormat.Char36,
                    "guid-uuid" => MySqlGuidFormat.Uuid,
                    _ => MySqlGuidFormat.Binary16,
                });
                entity.HasIndex("A", "B", "G");

                return;

            case "prefix":
                first.HasMaxLength(568);
                entity.Property<string>("C").HasMaxLength(568);
                entity.HasIndex("A", "B").HasPrefixLength(0, 200);
                entity.HasIndex("B", "C");

                return;

            case "prefix-only-implicit":
                entity.Property<string>("C").IsRequired();
                entity.Property<string>("D").IsRequired();
                entity.HasIndex("A", "B", "C", "D").HasPrefixLength(50, 0, 0, 0);

                return;

            case "prefix-overlap":
            case "prefix-overlap-reverse":
            case "prefix-overlap-impossible":
                entity.Property<string>("C").IsRequired();
                entity.Property<string>("D").IsRequired();
                entity.Property<string>("E").HasMaxLength(
                    scenario == "prefix-overlap-impossible" ? 757 : 700);

                if (scenario == "prefix-overlap-reverse")
                {
                    entity.HasIndex("A", "E");
                }

                entity.HasIndex("A", "B", "C", "D").HasPrefixLength(50, 0, 0, 0);

                if (scenario != "prefix-overlap-reverse")
                {
                    entity.HasIndex("A", "E");
                }

                return;

            case "impossible-explicit":
                first.HasMaxLength(768);
                break;
            case "impossible-prefix":
                first.HasMaxLength(700);
                entity.HasIndex("A", "B").HasPrefixLength(0, 200);

                return;

            case "prefix-over-ceiling":
                first.HasMaxLength(1);
                entity.HasIndex("A", "B").HasPrefixLength(0, 256);

                return;

            default:
                throw new InvalidOperationException($"Unknown budget scenario '{scenario}'.");
        }

        entity.HasIndex("A", "B");
    }
}
