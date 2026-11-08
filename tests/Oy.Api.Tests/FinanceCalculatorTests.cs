using Oy.Api.Domain;

namespace Oy.Api.Tests;

public class FinanceCalculatorTests
{
    private static readonly DateOnly Sep = new(2026, 9, 1);

    private static TrainingProgram ForestSchool() => new()
    {
        Title = "3-4 Yaş Orman Okulu", Slug = "x", Type = ProgramType.WeeklyClass,
        StartDate = new(2026, 9, 7), EndDate = new(2027, 6, 30), DaysOfWeek = [1, 3],
        StartTime = new(10, 0), EndTime = new(13, 0), Capacity = 12, Price = 6500, PricePeriod = PricePeriod.Monthly,
    };

    [Fact]
    public void Schedule_generates_only_selected_weekdays()
    {
        var dates = ForestSchool().ScheduledDates().ToList();
        Assert.All(dates, d => Assert.Contains(d.DayOfWeek, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }));
        Assert.Equal(new DateOnly(2026, 9, 7), dates[0]);
        // Sep 2026 from the 7th: Mon 7,14,21,28 + Wed 9,16,23,30
        Assert.Equal(8, dates.Count(d => d.Month == 9 && d.Year == 2026));
    }

    [Fact]
    public void Monthly_fee_counts_full_month_for_each_touched_month()
    {
        var e = new Enrollment { ChildName = "a", ParentName = "b", Fee = 6500, FeePeriod = PricePeriod.Monthly, StartDate = new(2026, 9, 20), Status = EnrollmentStatus.Active };
        Assert.Equal(6500, FinanceCalculator.EnrollmentRevenue(e, new(2027, 6, 30), Sep));
        Assert.Equal(6500, FinanceCalculator.EnrollmentRevenue(e, new(2027, 6, 30), new(2027, 6, 1)));
        Assert.Equal(0, FinanceCalculator.EnrollmentRevenue(e, new(2027, 6, 30), new(2027, 7, 1)));
    }

    [Theory]
    [InlineData(EnrollmentStatus.Pending)]
    [InlineData(EnrollmentStatus.Cancelled)]
    public void Non_active_enrollments_bring_no_revenue(EnrollmentStatus status)
    {
        var e = new Enrollment { ChildName = "a", ParentName = "b", Fee = 6500, FeePeriod = PricePeriod.Monthly, StartDate = Sep, Status = status };
        Assert.Equal(0, FinanceCalculator.EnrollmentRevenue(e, new(2027, 6, 30), Sep));
    }

    [Fact]
    public void One_time_fee_only_in_start_month()
    {
        var e = new Enrollment { ChildName = "a", ParentName = "b", Fee = 14500, FeePeriod = PricePeriod.OneTime, StartDate = new(2027, 7, 6), Status = EnrollmentStatus.Active };
        Assert.Equal(14500, FinanceCalculator.EnrollmentRevenue(e, new(2027, 7, 10), new(2027, 7, 1)));
        Assert.Equal(0, FinanceCalculator.EnrollmentRevenue(e, new(2027, 7, 10), new(2027, 8, 1)));
    }

    [Fact]
    public void Salaried_staff_fixed_cost_is_allocated_and_prorated()
    {
        var staff = new Staff { FullName = "x", EmploymentType = EmploymentType.Salaried, MonthlyNetSalary = 30000, MonthlySgkPremium = 9000, MonthlyTaxAndOther = 6000, PerLessonFee = 999 };
        var a = new StaffAssignment { StartDate = new(2026, 9, 16), EndDate = new(2027, 6, 30), SalaryAllocationPercent = 50 };

        var full = FinanceCalculator.AssignmentCost(a, staff, [], new(2026, 10, 1));
        Assert.Equal(22500, full.FixedCost);
        Assert.Equal(0, full.LessonCost); // salaried: per-lesson fee ignored

        var half = FinanceCalculator.AssignmentCost(a, staff, [], Sep); // 15 of 30 days
        Assert.Equal(11250, half.FixedCost);
    }

    [Fact]
    public void Per_lesson_staff_paid_for_held_sessions_inside_assignment()
    {
        var staff = new Staff { FullName = "x", EmploymentType = EmploymentType.PerLesson, PerLessonFee = 750, MonthlyNetSalary = 50000 };
        var a = new StaffAssignment { StartDate = new(2026, 9, 10), EndDate = new(2027, 6, 30), PerLessonFeeOverride = null };
        DateOnly[] held = [new(2026, 9, 7), new(2026, 9, 9), new(2026, 9, 14), new(2026, 9, 16)];

        var cost = FinanceCalculator.AssignmentCost(a, staff, held, Sep);
        Assert.Equal(0, cost.FixedCost);
        Assert.Equal(2, cost.LessonCount);
        Assert.Equal(1500, cost.LessonCost);

        a.PerLessonFeeOverride = 1000;
        Assert.Equal(2000, FinanceCalculator.AssignmentCost(a, staff, held, Sep).LessonCost);
    }

    [Fact]
    public void Program_finance_aggregates_revenue_costs_and_expenses()
    {
        var p = ForestSchool();
        p.Sessions = p.ScheduledDates().Select(d => new Session { Date = d }).ToList();
        p.Sessions[0].IsCancelled = true; // Sep 7 rained out
        var staff = new Staff { FullName = "x", EmploymentType = EmploymentType.PerLesson, PerLessonFee = 500 };
        p.Assignments = [new StaffAssignment { Staff = staff, StartDate = p.StartDate, EndDate = p.EndDate }];
        p.Enrollments = Enumerable.Range(0, 10).Select(_ => new Enrollment
        {
            ChildName = "c", ParentName = "p", Fee = 6500, FeePeriod = PricePeriod.Monthly, StartDate = p.StartDate, Status = EnrollmentStatus.Active,
        }).ToList();
        p.Expenses = [new Expense { Date = new(2026, 9, 10), Amount = 1200 }];

        var months = FinanceCalculator.ProgramFinance(p);

        Assert.Equal(10, months.Count); // Sep 2026 .. Jun 2027
        var sep = months[0];
        Assert.Equal("2026-09", sep.Month);
        Assert.Equal(65000, sep.Revenue);
        Assert.Equal(7 * 500, sep.StaffCost);
        Assert.Equal(1200, sep.Expenses);
        Assert.Equal(65000 - 3500 - 1200, sep.Profit);
    }
}
