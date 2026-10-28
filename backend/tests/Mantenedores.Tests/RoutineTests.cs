using Mantenedores.Core.Overrides;
using Mantenedores.Core.Schema;
using Mantenedores.Providers.MySql;
using Mantenedores.Providers.Postgres;
using Mantenedores.Providers.SqlServer;

namespace Mantenedores.Tests;

/// <summary>Acciones con procedimientos (P-061): qué rutinas se ofrecen y cómo se llaman en cada motor.</summary>
public class RoutineTests
{
    private static RawRoutineParameter P(string id, string name, string? param, int ord, bool output = false, RoutineKind kind = RoutineKind.Procedure) =>
        new(id, "ventas", name, kind, param, ord, "int", LogicalType.Integer, output);

    [Fact]
    public void Se_descartan_las_sobrecargas_las_de_salida_y_las_de_parametros_sin_nombre()
    {
        var routines = RoutineAssembler.Assemble([
            P("1", "subir", "id", 1), P("1", "subir", "monto", 2),
            P("2", "sin_parametros", null, 0),
            P("3", "doble", "a", 1), P("4", "doble", "b", 1),           // sobrecarga
            P("5", "con_salida", "id", 1), P("5", "con_salida", "total", 2, output: true),
            P("6", "fn_tabla", "id", 1, kind: RoutineKind.Function), P("6", "fn_tabla", "col", 2, output: true, kind: RoutineKind.Function),
            P("7", "sin_nombre", "", 1, kind: RoutineKind.Function),
            new RawRoutineParameter("8", "otro", "ajena", RoutineKind.Procedure, null, 0, null, LogicalType.Unsupported, false)
        ], ["ventas"]);

        Assert.Equal(["fn_tabla", "sin_parametros", "subir"], routines.Select(r => r.Name).ToList());
        Assert.Equal(["id", "monto"], routines.Single(r => r.Name == "subir").Parameters.Select(p => p.Name).ToList());
        Assert.Equal(["id"], routines.Single(r => r.Name == "fn_tabla").Parameters.Select(p => p.Name).ToList()); // la salida de la función no se pasa
    }

    [Fact]
    public void Valida_que_cada_parametro_tenga_de_donde_salir()
    {
        var routines = new[] { new RoutineInfo("ventas", "subir", RoutineKind.Procedure,
            [new RoutineParameter("id", 1, "int", LogicalType.Integer), new RoutineParameter("monto", 2, "int", LogicalType.Integer)]) };
        TableAction A(Dictionary<string, ActionParameter> p, string routine = "ventas.subir") => new() { Name = "a", Routine = routine, Parameters = p };

        Assert.Empty(OverrideApplier.ValidateActions([A(new() { ["id"] = new() { Column = "id" }, ["monto"] = new() { Value = "5" } })], routines));
        Assert.Contains("monto", OverrideApplier.ValidateActions([A(new() { ["id"] = new() { Column = "id" } })], routines)["actions.0"]);
        Assert.Contains("'x'", OverrideApplier.ValidateActions([A(new() { ["id"] = new() { Ask = true }, ["monto"] = new() { Ask = true }, ["x"] = new() { Value = "1" } })], routines)["actions.0"]);
        Assert.Contains("no existe", OverrideApplier.ValidateActions([A(new(), "ventas.otra")], routines)["actions.0"]);
    }

    [Fact]
    public void Cada_motor_llama_a_su_manera()
    {
        var args = new List<(RoutineParameter, string)> { (new RoutineParameter("id", 1, "integer", LogicalType.Integer), "@p0"), (new RoutineParameter("monto", 2, "numeric", LogicalType.Decimal), "@p1") };
        Assert.Equal("EXEC [ventas].[subir] @id = @p0, @monto = @p1", new SqlServerDialect().CallRoutine("[ventas].[subir]", RoutineKind.Procedure, args));
        Assert.Equal("CALL \"ventas\".\"subir\"(\"id\" => CAST(@p0 AS integer), \"monto\" => CAST(@p1 AS numeric))",
            new PostgresDialect().CallRoutine("\"ventas\".\"subir\"", RoutineKind.Procedure, args));
        Assert.Equal("SELECT * FROM \"ventas\".\"subir\"(\"id\" => CAST(@p0 AS integer), \"monto\" => CAST(@p1 AS numeric))",
            new PostgresDialect().CallRoutine("\"ventas\".\"subir\"", RoutineKind.Function, args));
        Assert.Equal("CALL `ventas`.`subir`(@p0, @p1)", new MySqlDialect().CallRoutine("`ventas`.`subir`", RoutineKind.Procedure, args));
        Assert.Equal("SELECT `ventas`.`subir`(@p0, @p1) AS `resultado`", new MySqlDialect().CallRoutine("`ventas`.`subir`", RoutineKind.Function, args));
        Assert.Throws<InvalidOperationException>(() => new SqlServerDialect().CallRoutine("[x]", RoutineKind.Procedure,
            [(new RoutineParameter("a; DROP TABLE x", 1, "int", LogicalType.Integer), "@p0")]));
    }
}
