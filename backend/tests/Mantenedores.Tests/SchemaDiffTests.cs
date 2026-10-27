using System.Text.Json;
using Mantenedores.Core.Schema;

namespace Mantenedores.Tests;

/// <summary>Diferencias de esquema (P-013): qué cambió desde la foto de referencia.</summary>
public class SchemaDiffTests
{
    private static readonly SchemaSnapshot Before = SchemaSnapshot.From(TestSchema.Build());

    private static SchemaSnapshot Change(Func<SnapshotTable, SnapshotTable?> producto, params SnapshotTable[] extra) =>
        new([.. Before.Tables.Select(t => t.Name == "producto" ? producto(t) : t).OfType<SnapshotTable>(), .. extra]);

    [Fact]
    public void Sin_cambios_no_hay_diferencias()
    {
        Assert.Empty(SchemaDiff.Compare(Before, SchemaSnapshot.From(TestSchema.Build())));
    }

    [Fact]
    public void Detecta_columnas_nuevas_eliminadas_tipo_y_nulos()
    {
        var after = Change(t => t with
        {
            Columns =
            [
                .. t.Columns
                    .Where(c => c.Name != "notas")
                    .Select(c => c.Name switch
                    {
                        "codigo" => c with { Type = "varchar(40)" },
                        "precio" => c with { IsNullable = true },
                        _ => c
                    }),
                new SnapshotColumn("stock", "int4", false)
            ]
        });

        var changes = SchemaDiff.Compare(Before, after);
        Assert.Collection(changes,
            c => Assert.Equal((SchemaChangeKind.ColumnAdded, "stock", "Columna nueva (int4, obligatoria)"), (c.Kind, c.Column, c.Description)),
            c => Assert.Equal((SchemaChangeKind.ColumnRemoved, "notas"), (c.Kind, c.Column)),
            c => Assert.Equal((SchemaChangeKind.ColumnTypeChanged, "codigo", "Tipo: varchar(20) → varchar(40)"), (c.Kind, c.Column, c.Description)),
            c => Assert.Equal((SchemaChangeKind.ColumnNullabilityChanged, "precio", "Ahora acepta vacíos"), (c.Kind, c.Column, c.Description)));
        Assert.All(changes, c => Assert.Equal(("ventas", "producto"), (c.Schema, c.Table)));
    }

    [Fact]
    public void Detecta_tablas_pk_y_relaciones()
    {
        var nueva = new SnapshotTable("ventas", "stock", TableKind.Table, [new("id", "int4", false)], ["id"], []);
        var after = Change(t => t with { PrimaryKey = ["id", "codigo"], ForeignKeys = [] }, nueva);
        after = after with { Tables = after.Tables.Where(t => t.Name != "v_productos").ToList() };

        var kinds = SchemaDiff.Compare(Before, after).Select(c => (c.Kind, c.Table)).ToList();
        Assert.Contains((SchemaChangeKind.TableAdded, "stock"), kinds);
        Assert.Contains((SchemaChangeKind.TableRemoved, "v_productos"), kinds);
        Assert.Contains((SchemaChangeKind.PrimaryKeyChanged, "producto"), kinds);
        Assert.Contains((SchemaChangeKind.ForeignKeyRemoved, "producto"), kinds);
        Assert.Equal(4, kinds.Count);
    }

    [Fact]
    public void La_foto_viaja_como_json_sin_perder_datos()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var back = JsonSerializer.Deserialize<SchemaSnapshot>(JsonSerializer.Serialize(Before, o), o)!;
        Assert.Empty(SchemaDiff.Compare(Before, back));
        Assert.Equal("varchar(20)", back.Tables.First(t => t.Name == "producto").Columns.First(c => c.Name == "codigo").Type);
    }
}
