using System.Text.Json;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Tests;

public class ValueConverterTests
{
    private static ColumnInfo C(LogicalType type, string native = "x", bool nullable = false, int? max = null, bool tz = false) =>
        new() { Name = "c", NativeType = native, Type = type, IsNullable = nullable, MaxLength = max, HasTimeZone = tz };

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Convierte_tipos_basicos()
    {
        Assert.Equal(5, ValueConverter.FromJson(C(LogicalType.Integer, "int"), J("5")));
        Assert.Equal(5L, ValueConverter.FromJson(C(LogicalType.Integer, "bigint"), J("\"5\"")));
        Assert.Equal(10.5m, ValueConverter.FromJson(C(LogicalType.Decimal, "numeric"), J("10.5")));
        Assert.Equal(true, ValueConverter.FromJson(C(LogicalType.Boolean), J("true")));
        Assert.Equal(new DateTime(2026, 1, 31), ValueConverter.FromString(C(LogicalType.Date), "2026-01-31"));
        Assert.Equal(new TimeSpan(8, 30, 0), ValueConverter.FromString(C(LogicalType.Time), "08:30"));
        Assert.Equal("{\"a\":1}", ValueConverter.FromJson(C(LogicalType.Json), J("{\"a\":1}")));
    }

    [Fact]
    public void Fecha_con_zona_horaria_se_normaliza_a_utc()
    {
        var v = (DateTimeOffset)ValueConverter.FromString(C(LogicalType.DateTime, tz: true), "2026-01-31T10:00:00-03:00")!;
        Assert.Equal(TimeSpan.Zero, v.Offset);
        Assert.Equal(13, v.Hour);
    }

    [Fact]
    public void Valida_obligatorios_largo_y_formato()
    {
        Assert.Throws<ValidationException>(() => ValueConverter.FromJson(C(LogicalType.String), J("null")));
        Assert.Null(ValueConverter.FromJson(C(LogicalType.String, nullable: true), J("null")));
        Assert.Throws<ValidationException>(() => ValueConverter.FromString(C(LogicalType.String, max: 3), "abcd"));
        Assert.Throws<ValidationException>(() => ValueConverter.FromString(C(LogicalType.Integer, "int"), "1.5"));
        Assert.Throws<ValidationException>(() => ValueConverter.FromString(C(LogicalType.Integer, "smallint"), "40000"));
        Assert.Throws<ValidationException>(() => ValueConverter.FromString(C(LogicalType.Json), "{malo"));
    }

    [Fact]
    public void Texto_vacio_en_campo_no_texto_es_nulo()
    {
        Assert.Null(ValueConverter.FromString(C(LogicalType.Integer, nullable: true), ""));
        Assert.Equal("", ValueConverter.FromString(C(LogicalType.String), ""));
    }
}
