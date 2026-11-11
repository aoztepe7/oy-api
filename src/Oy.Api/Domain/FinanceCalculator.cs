namespace Oy.Api.Domain;

public record AssignmentMonthCost(decimal FixedCost, int LessonCount, decimal LessonCost)
{
    public decimal Total => FixedCost + LessonCost;
}

public record ProgramMonthFinance(string Month, decimal Revenue, decimal StaffCost, decimal Expenses)
{
    public decimal Profit => Revenue - StaffCost - Expenses;
}

/// <summary>
/// Pure cost / revenue rules. All inputs must be loaded by the caller.
/// Revenue: Monthly fee counted in full for each calendar month the enrollment touches; OneTime in start month.
/// Staff: fixed monthly cost × allocation %, prorated by overlapping days; plus per-lesson fee × held sessions.
/// </summary>
public static class FinanceCalculator
{
    public static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);
    public static DateOnly MonthEnd(DateOnly d) => MonthStart(d).AddMonths(1).AddDays(-1);
    public static string MonthKey(DateOnly d) => $"{d.Year:D4}-{d.Month:D2}";

    public static IEnumerable<DateOnly> Months(DateOnly from, DateOnly to)
    {
        for (var m = MonthStart(from); m <= to; m = m.AddMonths(1)) yield return m;
    }

    /// <summary>Share of <paramref name="month"/> covered by [start, end], 0..1.</summary>
    public static decimal OverlapRatio(DateOnly start, DateOnly end, DateOnly month)
    {
        var first = MonthStart(month);
        var last = MonthEnd(month);
        var from = start > first ? start : first;
        var to = end < last ? end : last;
        if (to < from) return 0;
        return (decimal)(to.DayNumber - from.DayNumber + 1) / (last.DayNumber - first.DayNumber + 1);
    }

    /// <summary>Fee of the month; a month with a manual amount (Ödeme takibi → özel fiyat) counts that amount.</summary>
    public static decimal EnrollmentRevenue(Enrollment e, DateOnly programEnd, DateOnly month)
    {
        if (!e.CountsAsRevenue) return 0;
        var fee = e.DueAdjustments.FirstOrDefault(a => a.Period == MonthStart(month))?.Amount ?? e.Fee;
        if (e.FeePeriod == PricePeriod.OneTime)
            return MonthStart(e.StartDate) == MonthStart(month) ? fee : 0;
        var end = e.EndDate ?? programEnd;
        return OverlapRatio(e.StartDate, end, month) > 0 ? fee : 0;
    }

    public static AssignmentMonthCost AssignmentCost(
        StaffAssignment a, Staff staff, IEnumerable<DateOnly> heldSessionDates, DateOnly month)
    {
        var ratio = OverlapRatio(a.StartDate, a.EndDate, month);
        if (ratio == 0) return new(0, 0, 0);

        var fixedCost = Math.Round(staff.MonthlyFixedCost * a.SalaryAllocationPercent / 100m * ratio, 2);
        var first = MonthStart(month);
        var last = MonthEnd(month);
        var lessons = heldSessionDates.Count(d => d >= first && d <= last && d >= a.StartDate && d <= a.EndDate);
        var lessonCost = lessons * staff.EffectiveLessonFee(a.PerLessonFeeOverride);
        return new(fixedCost, lessons, lessonCost);
    }

    /// <summary>Needs Enrollments, Sessions, Expenses and Assignments (with Staff) loaded.</summary>
    public static List<ProgramMonthFinance> ProgramFinance(TrainingProgram p)
    {
        var held = p.Sessions.Where(s => !s.IsCancelled).Select(s => s.Date).ToList();
        var lastDate = new[] { p.EndDate }
            .Concat(p.Enrollments.Select(e => e.EndDate ?? e.StartDate))
            .Concat(p.Expenses.Select(e => e.Date))
            .Max();
        var firstDate = new[] { p.StartDate }
            .Concat(p.Enrollments.Select(e => e.StartDate))
            .Concat(p.Expenses.Select(e => e.Date))
            .Min();

        return Months(firstDate, lastDate).Select(m => new ProgramMonthFinance(
            MonthKey(m),
            Revenue: p.Enrollments.Sum(e => EnrollmentRevenue(e, p.EndDate, m)),
            StaffCost: p.Assignments.Sum(a => AssignmentCost(a, a.Staff, held, m).Total),
            Expenses: p.Expenses.Where(e => MonthStart(e.Date) == m).Sum(e => e.Amount)
        )).ToList();
    }
}
