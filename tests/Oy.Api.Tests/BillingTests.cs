using Oy.Api.Domain;

namespace Oy.Api.Tests;

public class BillingTests
{
    private static Enrollment Monthly(DateOnly start, DateOnly? end = null, decimal fee = 3000) => new()
    {
        ChildName = "Ada Yılmaz", ParentName = "Ayşe Yılmaz", ParentEmail = "ayse@ornek.com",
        Fee = fee, FeePeriod = PricePeriod.Monthly, StartDate = start, EndDate = end, Status = EnrollmentStatus.Active,
        Program = new TrainingProgram { Title = "Orman Okulu", Slug = "orman" },
    };

    [Fact]
    public void Monthly_fee_has_one_due_per_month_not_before_the_start()
    {
        var dues = Billing.DuesOf(Monthly(new DateOnly(2026, 9, 15)), programEnd: new DateOnly(2026, 12, 20), dueDay: 5);
        Assert.Equal(4, dues.Count);
        Assert.Equal(new DateOnly(2026, 9, 15), dues[0].DueDate); // starts after the 5th: due on the start date
        Assert.Equal(new DateOnly(2026, 10, 5), dues[1].DueDate);
        Assert.Equal(new DateOnly(2026, 12, 1), dues[3].Period);
        Assert.All(dues, d => Assert.Equal(3000, d.Amount));
    }

    [Fact]
    public void Due_day_is_clamped_to_the_month_end_and_one_time_fees_have_a_single_due()
    {
        var dues = Billing.DuesOf(Monthly(new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28)), new DateOnly(2027, 6, 1), dueDay: 31);
        Assert.Equal(new DateOnly(2027, 2, 28), Assert.Single(dues).DueDate);

        var camp = Monthly(new DateOnly(2026, 7, 1));
        camp.FeePeriod = PricePeriod.OneTime;
        var due = Assert.Single(Billing.DuesOf(camp, new DateOnly(2026, 7, 10), 5));
        Assert.Equal(new DateOnly(2026, 7, 1), due.DueDate);

        camp.Fee = 0; // sibling in a family package
        Assert.Empty(Billing.DuesOf(camp, new DateOnly(2026, 7, 10), 5));
    }

    [Fact]
    public void Payments_pay_their_period_and_unassigned_ones_fill_the_oldest_dues()
    {
        var e = Monthly(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 30));
        var dues = Billing.DuesOf(e, new DateOnly(2026, 11, 30), 5);
        var payments = new[]
        {
            new Payment { Amount = 3000, Period = new DateOnly(2026, 10, 1), PaidAt = new DateTime(2026, 10, 3) },
            new Payment { Amount = 4000, PaidAt = new DateTime(2026, 9, 2) }, // no period
        };
        var states = Billing.Allocate(dues, payments);
        Assert.Equal(DueStatus.Paid, states[0].Status);     // September from the unassigned payment
        Assert.Equal(DueStatus.Paid, states[1].Status);     // October by period
        Assert.Equal(DueStatus.Partial, states[2].Status);  // November: the remaining 1000
        Assert.Equal(2000, states[2].Remaining);
    }

    [Fact]
    public void A_single_month_can_have_its_own_price_and_due_date()
    {
        // Starts mid-November: November is charged in part, due with December's fee on the 5th.
        var e = Monthly(new DateOnly(2026, 11, 16), new DateOnly(2027, 1, 31));
        e.DueAdjustments.Add(new DueAdjustment { Period = new DateOnly(2026, 11, 1), Amount = 1500, DueDate = new DateOnly(2026, 12, 5), Note = "Yarım ay" });
        var dues = Billing.DuesOf(e, new DateOnly(2027, 6, 30), 5);

        Assert.Equal(3, dues.Count);
        Assert.Equal(1500, dues[0].Amount);
        Assert.Equal(3000, dues[0].StandardAmount);
        Assert.True(dues[0].Adjusted);
        Assert.Equal(new DateOnly(2026, 12, 5), dues[0].DueDate);
        Assert.Equal("Yarım ay", dues[0].Note);
        Assert.False(dues[1].Adjusted);
        Assert.Equal(3000, dues[1].Amount);

        // Revenue of the month follows the manual price.
        Assert.Equal(1500, FinanceCalculator.EnrollmentRevenue(e, new DateOnly(2027, 6, 30), new DateOnly(2026, 11, 1)));
        Assert.Equal(3000, FinanceCalculator.EnrollmentRevenue(e, new DateOnly(2027, 6, 30), new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void A_free_month_is_paid_without_a_payment()
    {
        var e = Monthly(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31));
        e.DueAdjustments.Add(new DueAdjustment { Period = new DateOnly(2026, 9, 1), Amount = 0 });
        var states = Billing.Allocate(Billing.DuesOf(e, new DateOnly(2026, 10, 31), 5), []);
        Assert.Equal(DueStatus.Paid, states[0].Status);
        Assert.Equal(DueStatus.Unpaid, states[1].Status);
    }

    [Theory]
    [InlineData(2026, 11, 16, 1500)] // 15 of 30 days
    [InlineData(2026, 11, 1, 3000)]  // whole month
    [InlineData(2026, 12, 20, 1161)] // 12 of 31 days
    public void Prorated_fee_follows_the_days_covered(int y, int m, int startDay, decimal expected) =>
        Assert.Equal(expected, Billing.ProratedFee(3000, new DateOnly(y, m, 1), new DateOnly(y, m, startDay), null));

    [Theory]
    [InlineData(3000, 10, 2727.27, 272.73)]
    [InlineData(3000, 20, 2500, 500)]
    [InlineData(1999.99, 20, 1666.66, 333.33)]
    [InlineData(100, 0, 100, 0)]
    public void Vat_inclusive_amounts_split_into_net_and_vat(decimal gross, decimal rate, decimal net, decimal vat)
    {
        var (n, v) = Billing.SplitVat(gross, rate);
        Assert.Equal(net, n);
        Assert.Equal(vat, v);
        Assert.Equal(gross, n + v);
    }

    [Fact]
    public void Invoice_draft_uses_billing_info_when_given()
    {
        var e = Monthly(new DateOnly(2026, 9, 1));
        var payment = new Payment { Enrollment = e, Amount = 3000, Period = new DateOnly(2026, 11, 1) };
        var settings = new BillingSettings { VatRate = 10, ItemName = "{program} eğitim hizmeti - {cocuk} {donem}" };

        var draft = Billing.Draft(payment, settings, new DateOnly(2026, 11, 6));
        Assert.False(draft.Buyer.FromBillingInfo);
        Assert.Equal("Ayşe Yılmaz", draft.Buyer.Name);
        Assert.Equal("11111111111", draft.Buyer.TaxId);
        Assert.Equal("Orman Okulu eğitim hizmeti - Ada Yılmaz (Kasım 2026)", draft.ItemName);
        Assert.Equal(2727.27m, draft.UnitPrice);
        Assert.Equal(272.73m, draft.VatAmount);
        Assert.Equal(3000, draft.Total);

        e.Billing = new BillingInfo { Kind = BillingKind.Corporate, Name = "Yılmaz Ltd.", TaxId = "1234567890", TaxOffice = "Kadıköy" };
        draft = Billing.Draft(payment, settings, new DateOnly(2026, 11, 6));
        Assert.True(draft.Buyer.FromBillingInfo);
        Assert.Equal("1234567890", draft.Buyer.TaxId);
        Assert.Equal("ayse@ornek.com", draft.Buyer.Email); // falls back to the parent's e-mail
    }

    [Fact]
    public void Item_name_without_period_has_no_empty_brackets()
    {
        Assert.Equal("Yaz Kampı eğitim hizmeti - Can", Billing.Fill("{program} eğitim hizmeti - {cocuk} {donem}", "Yaz Kampı", "Can", "", ""));
    }
}
