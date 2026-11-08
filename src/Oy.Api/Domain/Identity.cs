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
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string ProgramsRead = "programs.read";
    public const string ProgramsWrite = "programs.write";
    public const string StaffRead = "staff.read";
    public const string StaffWrite = "staff.write";
    public const string EnrollmentsRead = "enrollments.read";
    public const string EnrollmentsWrite = "enrollments.write";
    public const string FinanceRead = "finance.read";
    public const string FinanceWrite = "finance.write";
    public const string ContentWrite = "content.write";
    public const string SettingsManage = "settings.manage";

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [UsersManage] = "Kullanıcı ve rolleri yönet",
        [ProgramsRead] = "Eğitimleri görüntüle",
        [ProgramsWrite] = "Eğitimleri düzenle",
        [StaffRead] = "Personeli görüntüle",
        [StaffWrite] = "Personeli düzenle",
        [EnrollmentsRead] = "Kayıtları görüntüle",
        [EnrollmentsWrite] = "Kayıtları düzenle",
        [FinanceRead] = "Finansı görüntüle",
        [FinanceWrite] = "Finans kayıtlarını düzenle",
        [ContentWrite] = "Site içeriğini yönet",
        [SettingsManage] = "Entegrasyon ayarlarını yönet",
    };

    /// <summary>Policy name for media upload: any of these permissions is enough.</summary>
    public const string MediaUpload = "media.upload";
}
