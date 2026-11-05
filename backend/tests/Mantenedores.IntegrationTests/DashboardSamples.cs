using Mantenedores.Core.Dashboards;
using Mantenedores.Core.Query;

namespace Mantenedores.IntegrationTests;

/// <summary>Un dashboard con todo lo que se guarda, para probar los almacenes (P-128).</summary>
public static class DashboardSamples
{
    public static Dashboard One(string id) => new()
    {
        Id = id, Name = "Ventas", Description = "Resumen del mes", Owner = "ana",
        CreatedAt = DateTimeOffset.Parse("2026-11-05T10:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2026-11-05T11:00:00Z"), UpdatedBy = "ana",
        Shares = [new DashboardShare("ventas", CanEdit: true)],
        Widgets =
        [
            new DashboardWidget
            {
                Id = "w1", Title = "Por categoría", Type = WidgetType.Bar, Size = WidgetSize.L,
                Connection = "demo", Schema = "ventas", Table = "producto",
                Query = new AggregateRequest
                {
                    Function = AggregateFunction.Sum, Column = "precio", Order = AggregateOrder.ValueDesc, Limit = 10,
                    GroupBy = [new AggregateGroup("creado_en", DateBucket.Month)],
                    Filters = new Dictionary<string, string> { ["codigo"] = "BEB" }, DateColumn = "creado_en"
                }
            }
        ]
    };

    public static void AssertSame(Dashboard expected, Dashboard? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Owner, actual.Owner);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.Shares, actual.Shares);
        var w = Assert.Single(actual.Widgets);
        Assert.Equal(WidgetType.Bar, w.Type);
        Assert.Equal(WidgetSize.L, w.Size);
        Assert.Equal(AggregateFunction.Sum, w.Query.Function);
        Assert.Equal(DateBucket.Month, Assert.Single(w.Query.GroupBy).Bucket);
        Assert.Equal("BEB", w.Query.Filters["codigo"]);
        Assert.Equal(10, w.Query.Limit);
    }
}
