namespace Oy.Api.Domain;

public class Role : Entity
{
    public required string Name { get; set; }
    public List<string> Permissions { get; set; } = [];
    public List<User> Users { get; set; } = [];
}

public class User : Entity
{
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public UserKind Kind { get; set; } = UserKind.Staff;
    public string Phone { get; set; } = "";
    /// <summary>Staff only; guardians have no role and no panel permissions.</summary>
    public Guid? RoleId { get; set; }
    public Role? Role { get; set; }
    /// <summary>The staff record this login belongs to (teachers): opens the classrooms of their assignments.</summary>
    public Guid? StaffId { get; set; }
    public Staff? Staff { get; set; }
    /// <summary>Guardians: the children (enrollments) they follow.</summary>
    public List<GuardianLink> GuardianLinks { get; set; } = [];
}

/// <summary>
/// One permission of the catalog. <paramref name="Requires"/> is the parent it builds on (granting the child
/// grants the parent too); detail permissions such as prices or salaries hide fields inside a screen.
/// </summary>
public record PermissionInfo(string Key, string Label, string Group, string Description, string? Requires = null);

public static class Permissions
{
    public const string DashboardView = "dashboard.view";
    public const string DashboardFinance = "dashboard.finance";
    public const string ProgramsRead = "programs.read";
    public const string ProgramsPricing = "programs.pricing";
    public const string ProgramsFinance = "programs.finance";
    public const string ProgramsWrite = "programs.write";
    public const string ClassroomUse = "classroom.use";
    public const string StaffRead = "staff.read";
    public const string StaffCompensation = "staff.compensation";
    public const string StaffWrite = "staff.write";
    public const string EnrollmentsRead = "enrollments.read";
    public const string EnrollmentsContact = "enrollments.contact";
    public const string EnrollmentsFees = "enrollments.fees";
    public const string EnrollmentsWrite = "enrollments.write";
    public const string GuardiansManage = "guardians.manage";
    public const string FeedManage = "feed.manage";
    public const string FinanceRead = "finance.read";
    public const string FinanceWrite = "finance.write";
    public const string ContentWrite = "content.write";
    public const string UsersManage = "users.manage";
    public const string SettingsManage = "settings.manage";

    /// <summary>The permission tree, in display order.</summary>
    public static readonly IReadOnlyList<PermissionInfo> Catalog =
    [
        new(DashboardView, "Panoyu görüntüle", "Pano", "Aktif eğitim ve kayıt sayıları, bekleyen işler, haftanın dersleri."),
        new(DashboardFinance, "Panoda gelir ve gideri gör", "Pano", "Bu ayın geliri, maliyeti ve net sonucu.", DashboardView),

        new(ProgramsRead, "Eğitimleri görüntüle", "Eğitimler", "Eğitimler, takvim, oturumlar, temalar ve özel günler (fiyatsız)."),
        new(ProgramsPricing, "Fiyatları görüntüle", "Eğitimler", "Eğitim ücreti ve fiyat tablosu.", ProgramsRead),
        new(ProgramsFinance, "Kârlılığı görüntüle", "Eğitimler", "Eğitimin gelir, gider ve kâr dökümü; Giderler ve Finans sekmeleri.", ProgramsRead),
        new(ProgramsWrite, "Eğitimleri düzenle", "Eğitimler", "Eğitim ekleme, düzenleme, oturum iptali ve personel atama. Fiyatları değiştirmek için “Fiyatları görüntüle” de gerekir.", ProgramsRead),
        new(ClassroomUse, "Yoklama ve gözlem notu", "Eğitimler", "Görevli olduğu derslerde yoklama alır ve canlı havuza not yazar."),

        new(StaffRead, "Personeli görüntüle", "Personel", "Ad, unvan, iletişim ve eğitim atamaları (ücretsiz)."),
        new(StaffCompensation, "Maaş ve maliyetleri görüntüle", "Personel", "Maaş, SGK, vergi, ders ücreti ve personel maliyet raporları.", StaffRead),
        new(StaffWrite, "Personeli düzenle", "Personel", "Personel ekleme ve düzenleme. Ücretleri değiştirmek için “Maaş ve maliyetleri görüntüle” de gerekir.", StaffRead),

        new(EnrollmentsRead, "Kayıtları görüntüle", "Kayıtlar", "Kayıt listesi, ön kayıtlar ve iletişim mesajları."),
        new(EnrollmentsContact, "Veli iletişim bilgilerini görüntüle", "Kayıtlar", "Veli telefonu ve e-posta adresi.", EnrollmentsRead),
        new(EnrollmentsFees, "Kayıt ücretlerini görüntüle", "Kayıtlar", "Kayıt ücreti, ödemeler ve kalan tutar.", EnrollmentsRead),
        new(EnrollmentsWrite, "Kayıtları düzenle", "Kayıtlar", "Kayıt ekleme, onaylama ve düzenleme.", EnrollmentsRead),
        new(GuardiansManage, "Veli hesaplarını yönet", "Kayıtlar", "Onaylı kayıtlara veli (anne, baba) giriş hesabı açma, şifre sıfırlama ve kaldırma.", EnrollmentsRead),

        new(FeedManage, "Veli akışını yönet", "Veli iletişimi", "Velilerin akışına görsel ve duyuru paylaşma. Ders fotoğraflarını sınıftaki öğretmenler paylaşır."),

        new(FinanceRead, "Finansı görüntüle", "Finans", "Finans sayfası: tüm eğitimlerin gelir-gider özeti, ödemeler ve faturalar."),
        new(FinanceWrite, "Finans kayıtlarını düzenle", "Finans", "Gider ve ödeme girişi, fatura kesme.", FinanceRead),

        new(ContentWrite, "Site içeriğini yönet", "Site", "Sayfalar, menüler ve site ayarları."),

        new(UsersManage, "Kullanıcı ve rolleri yönet", "Yönetim", "Kullanıcı hesapları, roller ve yetkiler."),
        new(SettingsManage, "Entegrasyon ayarlarını yönet", "Yönetim", "Ödeme ve e-fatura entegrasyonları."),
    ];

    public static readonly IReadOnlyDictionary<string, string> All = Catalog.ToDictionary(p => p.Key, p => p.Label);

    /// <summary>Adds the parents of every permission (a child is meaningless without its parent).</summary>
    public static List<string> WithParents(IEnumerable<string> permissions)
    {
        var set = new HashSet<string>(permissions);
        foreach (var p in set.ToList())
        {
            for (var parent = Catalog.FirstOrDefault(c => c.Key == p)?.Requires; parent is not null;
                 parent = Catalog.FirstOrDefault(c => c.Key == parent)?.Requires)
                set.Add(parent);
        }
        return Catalog.Select(c => c.Key).Where(set.Contains).ToList();
    }

    /// <summary>Policy name for media upload: any of these permissions is enough.</summary>
    public const string MediaUpload = "media.upload";

    /// <summary>Policy name for the classroom (attendance + notes): teachers, or anyone who manages programs.</summary>
    public const string Classroom = "classroom";

    /// <summary>Policy name for guardian (veli) logins.</summary>
    public const string Guardian = "guardian";

    /// <summary>Policy name for uploading private files (feed images, lesson photos).</summary>
    public const string PrivateUpload = "private.upload";
}
