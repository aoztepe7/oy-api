using Oy.Api.Domain;

namespace Oy.Api.Tests;

public class PermissionTests
{
    [Fact]
    public void Detail_permission_brings_its_parent()
    {
        Assert.Equal([Permissions.StaffRead, Permissions.StaffCompensation], Permissions.WithParents([Permissions.StaffCompensation]));
        Assert.Equal([Permissions.DashboardView, Permissions.DashboardFinance], Permissions.WithParents([Permissions.DashboardFinance]));
    }

    [Fact]
    public void Result_is_unique_and_in_catalog_order()
    {
        var result = Permissions.WithParents([Permissions.FinanceWrite, Permissions.ProgramsPricing, Permissions.ProgramsRead, Permissions.ProgramsPricing]);
        Assert.Equal([Permissions.ProgramsRead, Permissions.ProgramsPricing, Permissions.FinanceRead, Permissions.FinanceWrite], result);
    }

    [Fact]
    public void Catalog_parents_exist_and_keys_are_unique()
    {
        var keys = Permissions.Catalog.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(Permissions.Catalog.Where(p => p.Requires is not null), p => Assert.Contains(p.Requires!, keys));
    }
}
