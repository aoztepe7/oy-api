using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Domain;

namespace Oy.Api.Data;

public class SeedOptions
{
    public string AdminEmail { get; set; } = "oztepeali@gmail.com";
    public string AdminPassword { get; set; } = "@Ali1990";
}

/// <summary>
/// Idempotent seed: roles, admin, CMS pages, site settings and the real starting content
/// (camps and team from the previous okulumyeryuzu.com site).
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher<User> hasher, SeedOptions options, string mediaRoot)
    {
        SeedMedia.CopyTo(mediaRoot);
        await SeedRolesAndAdminAsync(db, hasher, options);
        await RemoveDemoDataAsync(db);
        await SeedPagesAsync(db);
        if (!await db.SpecialDays.AnyAsync())
        {
            db.SpecialDays.AddRange(SpecialDayCatalog.Build(2025, 2030));
            await db.SaveChangesAsync();
        }
        await SeedStaffAsync(db);
        await SeedProgramsAsync(db);
    }

    private static async Task SeedRolesAndAdminAsync(AppDbContext db, IPasswordHasher<User> hasher, SeedOptions o)
    {
        var roles = new Dictionary<string, List<string>>
        {
            ["Yönetici"] = [.. Permissions.All.Keys],
            ["Eğitim Koordinatörü"] =
            [
                Permissions.ProgramsRead, Permissions.ProgramsWrite, Permissions.StaffRead,
                Permissions.EnrollmentsRead, Permissions.EnrollmentsWrite,
            ],
            ["Muhasebe"] =
            [
                Permissions.ProgramsRead, Permissions.StaffRead, Permissions.EnrollmentsRead,
                Permissions.FinanceRead, Permissions.FinanceWrite,
            ],
            ["İçerik Editörü"] = [Permissions.ContentWrite, Permissions.ProgramsRead],
        };
        foreach (var (name, perms) in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name)) db.Roles.Add(new Role { Name = name, Permissions = perms });
        }
        await db.SaveChangesAsync();

        // Admin role always gets newly added permissions.
        var admin = await db.Roles.FirstAsync(r => r.Name == "Yönetici");
        if (Permissions.All.Keys.Except(admin.Permissions).Any())
        {
            admin.Permissions = [.. Permissions.All.Keys];
            await db.SaveChangesAsync();
        }

        // Seeded admin is ensured by email, so it is also created on databases that already have users.
        var adminEmail = o.AdminEmail.Trim().ToLowerInvariant();
        if (!await db.Users.AnyAsync(u => u.Email == adminEmail))
        {
            var user = new User { Email = adminEmail, FullName = "Ali Öztepe", RoleId = admin.Id };
            user.PasswordHash = hasher.HashPassword(user, o.AdminPassword);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
    }

    // ---- Demo data cleanup (earlier versions seeded lorem ipsum pages and dummy programs/staff) ----

    private const string LoremMarker = "Lorem ipsum";
    private const string DemoPhone = "+90 555 000 00 00";
    private static readonly string[] DemoProgramSlugs = ["3-4-yas-orman-okulu", "5-6-yas-orman-okulu", "yaz-doga-kampi", "aile-doga-bulusmasi"];
    private static readonly string[] DemoStaff = ["Ayşe Yılmaz", "Mehmet Demir", "Zeynep Kaya"];
    private const string DemoParentEmail = "veli@example.com";

    /// <summary>
    /// Removes the old dummy records, but only when they are untouched: a demo program is kept if it has
    /// any non-demo enrollment, a demo staff member is kept if still assigned somewhere.
    /// </summary>
    private static async Task RemoveDemoDataAsync(AppDbContext db)
    {
        var programs = await db.Programs
            .Where(p => DemoProgramSlugs.Contains(p.Slug) && p.Enrollments.All(e => e.ParentEmail == DemoParentEmail))
            .ToListAsync();
        db.Programs.RemoveRange(programs); // sessions, assignments, enrollments, expenses cascade
        await db.SaveChangesAsync();

        var staff = await db.Staff.Where(s => DemoStaff.Contains(s.FullName) && !s.Assignments.Any()).ToListAsync();
        db.Staff.RemoveRange(staff);

        var themes = await db.Themes
            .Where(t => (t.Title == "Ayın Teması: Toprak ve Tohumlar" || t.Title == "Haftanın Teması: Yaprakların Dili") && t.Description.StartsWith(LoremMarker))
            .ToListAsync();
        db.Themes.RemoveRange(themes);
        await db.SaveChangesAsync();
    }

    // ---- Pages and settings ----

    /// <summary>
    /// Creates missing built-in pages (published) and the site settings row. Pages and settings still holding
    /// the old placeholder content are replaced with the real content; edited ones are left alone.
    /// </summary>
    private static async Task SeedPagesAsync(AppDbContext db)
    {
        var pages = await db.Pages.ToDictionaryAsync(p => p.Slug);
        foreach (var (slug, title, blocks) in PageTemplates.SystemPages())
        {
            var (seoTitle, seoDescription) = PageTemplates.Seo(slug);
            if (!pages.TryGetValue(slug, out var page))
            {
                db.Pages.Add(new Page
                {
                    Slug = slug, Title = title, SeoTitle = seoTitle, SeoDescription = seoDescription, IsSystem = true,
                    Draft = blocks, Published = [.. blocks], PublishedAt = DateTime.UtcNow, DraftUpdatedAt = DateTime.UtcNow,
                });
            }
            else if (IsPlaceholder(page))
            {
                page.Title = title;
                page.SeoTitle = seoTitle;
                page.SeoDescription = seoDescription;
                page.Draft = blocks;
                page.Published = [.. blocks];
                page.PublishedAt = page.DraftUpdatedAt = DateTime.UtcNow;
            }
        }

        var settings = await db.SiteSettings.FirstOrDefaultAsync();
        if (settings is null) db.SiteSettings.Add(PageTemplates.Settings());
        else if (settings.Phone == DemoPhone)
        {
            var real = PageTemplates.Settings();
            settings.SiteName = real.SiteName;
            settings.Tagline = real.Tagline;
            settings.FooterText = real.FooterText;
            settings.Phone = real.Phone;
            settings.Email = real.Email;
            settings.Address = real.Address;
            settings.InstagramUrl = real.InstagramUrl;
            settings.HeaderCtaText = real.HeaderCtaText;
            settings.HeaderCtaHref = real.HeaderCtaHref;
            settings.HeaderNav = real.HeaderNav;
            settings.FooterNav = real.FooterNav;
        }
        await db.SaveChangesAsync();
    }

    private static bool IsPlaceholder(Page page) =>
        page.SeoDescription.StartsWith(LoremMarker) ||
        page.Draft.Any(b => b.Props.ToJsonString().Contains(LoremMarker, StringComparison.Ordinal));

    // ---- Team ----

    private static async Task SeedStaffAsync(AppDbContext db)
    {
        Staff[] team =
        [
            new()
            {
                FullName = "Pelin Öztepe", Title = "Kurucu, Orman Okulu Lideri", ShowOnWebsite = true,
                PhotoUrl = SeedMedia.PelinPhoto,
                Bio = "Çocuk gelişimi mezunu, doğa temelli eğitim uygulayıcısı ve lisanslı dağcı. 10 yılı aşkın süredir çocuklar, aileler, " +
                      "öğretmenler ve eğitim kurumlarıyla doğada öğrenme çalışmaları yürütüyor. Orman Okulu Derneği eğitmen eğitimlerini, " +
                      "Doğada Öğreniyorum programını ve bushcraft eğitimlerini tamamladı.",
            },
        ];
        foreach (var member in team)
        {
            if (!await db.Staff.AnyAsync(s => s.FullName == member.FullName)) db.Staff.Add(member);
        }
        await db.SaveChangesAsync();
    }

    // ---- Camps ----

    private static async Task SeedProgramsAsync(AppDbContext db)
    {
        foreach (var program in Camps())
        {
            if (await db.Programs.AnyAsync(p => p.Slug == program.Slug)) continue;
            program.Sessions = program.ScheduledDates()
                .Select(d => new Session { ProgramId = program.Id, Date = d, StartTime = program.StartTime, EndTime = program.EndTime })
                .ToList();
            db.Programs.Add(program);
        }
        await db.SaveChangesAsync();
    }

    private static IEnumerable<TrainingProgram> Camps()
    {
        yield return new TrainingProgram
        {
            Title = "Likya Yolu Aile Doğa Kampı", Slug = "likya-yolu-aile-doga-kampi", Type = ProgramType.Camp,
            AgeMin = 6, AgeMax = 10, StartDate = new DateOnly(2026, 11, 21), EndDate = new DateOnly(2026, 11, 22), DaysOfWeek = [6, 7],
            StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(17, 0), Capacity = 20, Price = 20000, PricePeriod = PricePeriod.OneTime,
            Location = "Karaöz, Korsan Koyu ve Gelidonya Feneri / Antalya", CoverImageUrl = SeedMedia.LyciaPoster,
            IsPublished = true, FeaturedOrder = 1,
            PriceOptions =
            [
                new("1 Yetişkin + 1 Çocuk", 20000), new("1 Yetişkin + 2 Çocuk", 23000),
                new("2 Yetişkin + 1 Çocuk", 24000), new("2 Yetişkin + 2 Çocuk", 27000),
                new("İlave çocuk", 3000, "Her ek çocuk için", IsAddon: true),
                new("İlave yetişkin", 3500, "Her ek yetişkin için", IsAddon: true),
            ],
            Summary = "Küçük adımlarla kadim yolların izinde! 6–10 yaş çocuklar ve aileleri için Gelidonya Feneri ve Korsan Koyu'nda iki günlük Likya Yolu yürüyüş ve keşif kampı.",
            Description = """
                Bu ara tatilde çocuklarımız ve ailelerimizle birlikte Likya Yolu'nun en özel noktalarından Gelidonya Feneri ve Korsan Koyu'nu keşfetmek üzere yola çıkıyoruz!

                Hayalimiz, çocuklarımızla Likya Yolu'nun patikalarını adım adım keşfetmek. Yalnızca bir yere ulaşmak için değil; yolun kendisini deneyimlemek, merak etmek, gözlemlemek, bazen yorulmak, birbirimize destek olmak ve birlikte başarmanın mutluluğunu yaşamak için.

                İki gün boyunca Likya Yolu'nda yürüyecek, kıyı ekosistemini ve Akdeniz'in bitki örtüsünü tanıyacak, doğa atölyelerine katılacak, yaratıcı drama çalışmaları yapacak ve birlikte unutulmaz anılar biriktireceğiz.

                ## Kamp bilgileri

                - **Tarih:** 21–22 Kasım 2026, Cumartesi–Pazar
                - **Bölge:** Karaöz, Korsan Koyu ve Gelidonya Feneri / Antalya
                - **Yaş grubu:** 6–10 yaş ve aileleri
                - **Süre:** 1 gece, 2 gün
                - **Konaklama:** Ayaz Bungalows — doğayla iç içe, konforlu ahşap evler
                - **Buluşma:** 21 Kasım Cumartesi, saat 11.00
                - **Katılım:** Aile katılımlı, kontenjanlı organizasyon

                ## Cumartesi

                - **11.00 Buluşma ve açılış çemberi:** Tanışma oyunları, kamp programının paylaşılması ve iki günlük maceramıza birlikte başlangıç.
                - **Yeryüzü Sofrası — aile pikniği:** Herkesin yanında getirdiği yiyeceklerle doğanın kalbinde uzun bir Yeryüzü sofrası kuruyoruz.
                - **Ahşap evlere yerleşme:** Konaklama alanımıza yerleşiyor ve kısa bir dinlenme molası veriyoruz.
                - **Likya Yolu — Korsan Koyu kısa parkur yürüyüşü:** Likya Yolu'nun patikalarında ilk adımlarımızı atıyor, doğayı gözlemliyor ve keşif molaları veriyoruz.
                - **Deniz kenarında doğa atölyesi:** Kıyı ekosistemini keşfediyor, deniz ve kara yaşamı arasındaki ilişkiyi inceliyor, doğal materyallerle yaratıcı çalışmalar yapıyoruz.
                - **18.00 Akşam yemeği**
                - **Yaratıcı drama atölyesi — Gelidonya Feneri Efsaneleri:** Çocuklarımız fenerin hikâyelerinden ilham alan canlandırmalar ve oyunlarla hayal güçlerini kullanırken aileler dinlenebilecek.
                - **Ateş başı aile buluşması:** Sohbetler, hikâyeler ve marshmallow eşliğinde kamp akşamı.
                - **21.30 İyi geceler:** Dinlenme ve ertesi günün büyük macerasına hazırlık.

                ## Pazar

                - **08.00 Kahvaltı ve yürüyüş hazırlığı**
                - **09.00 Gelidonya Feneri uzun parkur yürüyüşü:** Likya Yolu'nun eşsiz Akdeniz manzaraları eşliğinde fenere doğru yola çıkıyoruz. Amacımız yalnızca fenere ulaşmak değil; çocukların yol boyunca gözlem yapmalarına, merak etmelerine ve kendi tempolarında ilerlemelerine alan açmak.
                - **Gelidonya Feneri'nde ekolojik okuryazarlık atölyesi:** Akdeniz maki bitki örtüsünü tanıma, bölgeye özgü bitkileri gözlemleme, bitkilerin ekosistemdeki rollerini keşfetme.
                - **Manzaraya karşı kumanya molası**
                - **Dönüş yürüyüşü ve veda çemberi:** Kamp boyunca yaşanan deneyimleri ve keşifleri paylaşıyoruz.
                - **Likya Yolu etap rozeti töreni:** Çocuklarımıza yolculuklarının anısına özel etap rozetlerini takdim ediyoruz.

                *Program akışı ve saatleri, hava koşulları, parkur özellikleri ve çocuklarımızın ihtiyaçları doğrultusunda değişiklik gösterebilir.*

                ## Ücrete dahil olanlar

                - 1 gece ahşap evlerde konaklama
                - Cumartesi akşam yemeği
                - Pazar sabah kahvaltısı
                - İki günlük profesyonel rehber eşliğinde doğa yürüyüşleri
                - Ekolojik okuryazarlık ve doğa atölyeleri
                - Çocuklara özel yaratıcı drama etkinliği
                - Likya Yolu etap rozeti

                ## Ücrete dahil olmayanlar

                - Kamp alanına gidiş ve dönüş ulaşımı
                - Cumartesi aile pikniği için getirilecek yiyecekler
                - Pazar yürüyüşü için kumanya ve kişisel atıştırmalıklar
                - Yemeklerde tüketilecek içecekler
                - Kişisel harcamalar

                ## Önemli bilgiler

                Kampımız, 6–10 yaş çocuklar ve aileleri için hazırlanmış, sınırlı kontenjanlı bir doğa yürüyüşü ve keşif kampıdır. Katılacak çocukların ve yetişkinlerin doğa yürüyüşlerine ilgi duymalarını ve yürüyüş sürecine istekli olmalarını önemsiyoruz.

                Katılım için önceden doğa yürüyüşü deneyimi gerekmemektedir. Ancak Likya Yolu'nun doğal yapısı gereği parkurlarda taşlık, eğimli ve zaman zaman fiziksel olarak zorlayıcı bölümlerle karşılaşılabilir. Uzun süre yürümek ve yorulmak bu deneyimin doğal bir parçasıdır.

                Yürüyüşlerimiz çocukların yaş ve gelişim özellikleri gözetilerek, keşif ve dinlenme molalarıyla gerçekleştirilecektir. Ailelerimizin kendilerinin ve çocuklarının fiziksel yeterliliklerini değerlendirerek katılım sağlamalarını rica ediyoruz.

                Kampımız aile katılımlıdır. Çocukların güvenliği ve grup bütünlüğü için yürüyüş boyunca ebeveyn eşliği esastır.

                Kontenjanımız sınırlıdır. Kesin kayıt ve ödeme detayları başvuru sonrasında paylaşılacaktır. Kesin kayıt sonrasında ailelerimizle ayrıntılı bir kamp hazırlık ve bilgilendirme kılavuzu paylaşılacaktır.

                Çocuklarımızın hafızasında yalnızca gördükleri yerler değil; yürüdükleri yollar, aştıkları zorluklar, kurdukları dostluklar ve aileleriyle paylaştıkları anılar da yer edecek. **Likya Yolu'nda birlikte yeni hikâyeler biriktirmek için sizleri de aramıza bekliyoruz!**
                """,
        };

        yield return new TrainingProgram
        {
            Title = "Aile Macera Kampı", Slug = "aile-macera-kampi", Type = ProgramType.Camp,
            AgeMin = 5, AgeMax = 8, StartDate = new DateOnly(2026, 7, 4), EndDate = new DateOnly(2026, 7, 5), DaysOfWeek = [6, 7],
            StartTime = new TimeOnly(13, 0), EndTime = new TimeOnly(16, 0), Capacity = 20, Price = 12500, PricePeriod = PricePeriod.OneTime,
            Location = "Beşkonak, Manavgat / Antalya", CoverImageUrl = SeedMedia.FamilyCampPoster,
            IsPublished = true,
            PriceOptions =
            [
                new("1 Yetişkin + 1 Çocuk", 12500), new("2 Yetişkin + 1 Çocuk", 15000),
                new("İlave çocuk", 2500, "Her ek çocuk için", IsAddon: true),
            ],
            Summary = "Bushcraft atölyeleri, jeep safari, rafting ve gece yürüyüşüyle Beşkonak'ta ailece doğada yaşam deneyimi.",
            Description = """
                Doğayla yeniden bağ kurmak, birlikte öğrenmek, keşfetmek ve unutulmaz anılar biriktirmek için sizleri Aile Macera Kampımıza davet ediyoruz.

                Bu kamp bir tatil programından çok; ailece yavaşlayabileceğimiz, doğanın ritmine kulak verebileceğimiz ve doğada yaşam becerilerini deneyimleyebileceğimiz bir buluşma olarak tasarlandı.

                Çocuklarımızla birlikte yön bulacak, barınak yapacak, ateş yakacak, yemek pişirecek, gece ormanı deneyimleyecek, rafting ve jeep safari macerasına katılacağız.

                ## Cumartesi

                - **13.00 Karşılama ve yerleşme:** Tanışma çemberi ve oyunlar, mini çadır eğitimi, ailece çadırların kurulması.
                - **Yeryüzü pikniği:** Doğanın içinde ortak sofrada buluşuyor, tanışıyor, yavaşlıyor ve kampın ritmine giriyoruz.
                - **Aile bushcraft atölyesi I — Kaşiflerin Görevi:** Yön bulma, barınak yapma, takım çalışması ve görevler.
                - **Serbest zaman ve macera parkuru:** Zipline, ip parkurları, denge oyunları, serbest keşif.
                - **Akşam yemeği:** Izgara köfte, tereyağlı pilav, patates kızartması, süzme yoğurt, çoban salata ve mevsim meyvesi.
                - **Tazı Kanyonu gün batımı jeep safari:** Jeep safari, gün batımı keşfi, doğa ile bağ kurma atölyesi.
                - **Ateş çemberi:** Ateş başı sohbetler, hikâyeler ve paylaşımlar.
                - **Gece yürüyüşü:** Gece seslerini dinleme, gökyüzünü gözlemleme.

                ## Pazar

                - **Kahvaltı:** Domates, salatalık, reçel, peynir ve zeytin çeşitleri, haşlanmış yumurta, simit, poğaça, pişi ve kalem böreği.
                - **Bushcraft atölyesi II — Doğada yaşam becerileri:** Su arıtma yöntemleri, ateş yakma, güvenli bıçak kullanımı, közde soğanlı yumurta ve közde patates.
                - **Ailece rafting macerası:** Beşkonak'ın eşsiz manzarasında, profesyonel rehberler eşliğinde unutulmaz bir aile deneyimi.
                - **Öğle yemeği:** Izgara tavuk pirzola, bulgur pilavı, soslu makarna, çoban salata ve mevsim meyvesi.
                - **16.00 Veda çemberi:** Kampın değerlendirilmesi, paylaşımlar, doğaya teşekkür ve vedalaşma.

                ## Neler yapacağız?

                - Doğa ile bağ kuracağız.
                - Yön bulmayı deneyimleyeceğiz.
                - Barınak yapacağız.
                - Ateş yakacağız.
                - Güvenli alet kullanımını öğreneceğiz.
                - Gece doğasını keşfedeceğiz.
                - Jeep safari ve rafting gibi doğa sporlarını deneyimleyeceğiz.
                - Birlikte kaliteli zaman geçirip unutulmaz anılar biriktireceğiz.
                """,
        };
    }
}
