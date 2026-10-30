using Mantenedores.Core.Overrides;
using Mantenedores.Core.Schema;

namespace Mantenedores.Tests;

/// <summary>Sugerencias de ajustes sin IA (P-112), con un esquema como el de CentroCosto.</summary>
public class OverrideSuggesterTests
{
    private static ColumnInfo Col(string name, LogicalType type = LogicalType.Integer, bool pk = false) =>
        new() { Name = name, NativeType = type == LogicalType.Integer ? "int" : "varchar", Type = type, IsPrimaryKey = pk, IsNullable = !pk };

    private static TableInfo Table(string name, params ColumnInfo[] cols) => new()
    {
        Schema = "Comun", Name = name, Columns = cols, PrimaryKey = cols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList()
    };

    private static readonly TableInfo Empresa = Table("Empresa", Col("EmpresaID", pk: true), Col("Nombre", LogicalType.String));
    private static readonly TableInfo Area = Table("Area", Col("AreaID", pk: true), Col("EmpresaID"), Col("Nombre", LogicalType.String));
    private static readonly TableInfo TipoGasto = Table("TipoGasto", Col("TipoGastoID", pk: true), Col("Nombre", LogicalType.String));
    private static readonly TableInfo Centro = Table("CentroCosto",
        Col("CentroCostoID", pk: true), Col("EmpresaID"), Col("AreaID"), Col("TipoGastoID"), Col("Parent"), Col("Nombre", LogicalType.String),
        Col("CreationDate", LogicalType.DateTime), Col("HostName", LogicalType.String), Col("UserName", LogicalType.String),
        Col("CorreoContacto", LogicalType.String), Col("SinTablaID"));

    private static DatabaseSchema Schema(params TableInfo[] tables) => SchemaAssembler.Assemble([], [], []) with { Tables = tables };

    [Fact]
    public void Sugiere_relaciones_filtros_columnas_tecnicas_y_correos()
    {
        var s = OverrideSuggester.For(Schema(Empresa, Area, TipoGasto, Centro), Centro, null);
        var ids = s.Select(x => x.Id).ToList();

        Assert.Contains("relation:EmpresaID", ids);
        Assert.Contains("relation:AreaID", ids);
        Assert.Contains("relation:TipoGastoID", ids);
        Assert.DoesNotContain("relation:SinTablaID", ids); // no hay tabla SinTabla
        Assert.DoesNotContain("relation:Parent", ids);     // las autorreferencias no se adivinan
        var area = s.Single(x => x.Id == "relation:AreaID");
        Assert.Equal(("Comun", "Area", "AreaID"), (area.RefSchema, area.RefTable, area.RefColumn));

        // Area.EmpresaID apunta (por nombre) a Empresa, igual que CentroCosto.EmpresaID: filtro del combo.
        var filter = s.Single(x => x.Kind == SuggestionKind.LookupFilter);
        Assert.Equal(("AreaID", "EmpresaID", "EmpresaID"), (filter.Column, filter.RefColumn, filter.LocalColumn));
        Assert.Equal("EmpresaID", filter.Patch.LookupFilters!["AreaID"]["EmpresaID"]);
        Assert.Equal("relation:AreaID", filter.Requires); // la relación de AreaID también es sugerida

        // Validadas con el catálogo: el filtro queda porque se valida junto con su relación.
        var valid = OverrideSuggester.Valid(Schema(Empresa, Area, TipoGasto, Centro), "Comun", "CentroCosto", new TableOverride(), s);
        Assert.Contains(valid, x => x.Kind == SuggestionKind.LookupFilter);
        Assert.Equal(s.Count, valid.Count);

        Assert.Equal(["CreationDate", "HostName"], s.Where(x => x.Kind == SuggestionKind.Technical).Select(x => x.Column));
        Assert.Equal("CorreoContacto", Assert.Single(s, x => x.Kind == SuggestionKind.Email).Column);
    }

    [Fact]
    public void Lo_ya_ajustado_no_se_vuelve_a_sugerir_y_la_combinacion_suma()
    {
        var current = new TableOverride
        {
            LookupFilters = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["AreaID"] = new Dictionary<string, string> { ["EmpresaID"] = "EmpresaID" } },
            Columns = new Dictionary<string, ColumnOverride> { ["HostName"] = new() { ReadOnly = false } }
        };
        var s = OverrideSuggester.For(Schema(Empresa, Area, TipoGasto, Centro), Centro, current);
        Assert.DoesNotContain(s, x => x.Kind == SuggestionKind.LookupFilter);
        Assert.DoesNotContain(s, x => x.Id == "technical:HostName");

        var merged = s.Where(x => x.Kind is SuggestionKind.Relation or SuggestionKind.Technical)
            .Aggregate(current, (o, x) => OverrideSuggester.Merge(o, x.Patch));
        Assert.Equal(3, merged.VirtualForeignKeys!.Count);
        Assert.False(merged.Columns!["HostName"].ReadOnly); // lo del usuario se respeta
        Assert.True(merged.Columns!["CreationDate"].ReadOnly);
        Assert.Equal("EmpresaID", merged.LookupFilters!["AreaID"]["EmpresaID"]);
        // Aplicar dos veces la misma no duplica.
        Assert.Equal(3, OverrideSuggester.Merge(merged, s.First(x => x.Kind == SuggestionKind.Relation).Patch).VirtualForeignKeys!.Count);
    }

    [Fact]
    public void Dos_tablas_igual_de_probables_no_se_sugieren()
    {
        var otraEmpresa = Table("Empresa") with { Schema = "Otro", Columns = Empresa.Columns, PrimaryKey = Empresa.PrimaryKey };
        var centro = Centro with { Schema = "Tercero" };
        var s = OverrideSuggester.For(Schema(Empresa, otraEmpresa, centro), centro, null);
        Assert.DoesNotContain(s, x => x.Id == "relation:EmpresaID");
    }

    [Fact]
    public void Lo_mas_reciente_es_por_fecha_de_modificacion_creacion_o_pk()
    {
        var conFechas = Table("Mov", Col("MovID", pk: true), Col("creado_en", LogicalType.DateTime), Col("ModifiedDate", LogicalType.DateTime));
        Assert.Equal(["ModifiedDate", "MovID"], Mantenedores.Core.Services.CrudService.RecencyOrder(conFechas).Select(o => o.Column));
        var soloCreacion = Table("Mov", Col("MovID", pk: true), Col("creado_en", LogicalType.DateTime));
        var order = Mantenedores.Core.Services.CrudService.RecencyOrder(soloCreacion)[0];
        Assert.Equal(("creado_en", true), (order.Column, order.Descending));
        Assert.Equal("MovID", Assert.Single(Mantenedores.Core.Services.CrudService.RecencyOrder(Table("Mov", Col("MovID", pk: true), Col("Nombre", LogicalType.String)))).Column);
    }
}
