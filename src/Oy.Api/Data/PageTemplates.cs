using Oy.Api.Domain;
using Oy.Api.Features.Content;

namespace Oy.Api.Data;

/// <summary>
/// Starting content: built-in pages (seeded once) and the template for new pages.
/// Texts come from the previous okulumyeryuzu.com site; images from <see cref="SeedMedia"/>.
/// </summary>
public static class PageTemplates
{
    private static PageBlock B(string type, object props) => BlockContent.Create(type, props);

    public const string WhatsAppUrl = "https://wa.me/905425431993";
    private const string Tagline = "Çocukların doğayla, kendileriyle ve yaşamla bağ kurabilecekleri alanlar açıyoruz.";

    /// <summary>A new page: header + text block to start typing into.</summary>
    public static List<PageBlock> Content(string title) =>
    [
        B("pageHeader", new { title, intro = "" }),
        B("text", new { body = "" }),
    ];

    private static readonly NavItem[] Nav =
    [
        new("Kamp ve etkinlikler", "/etkinlikler"), new("Programlar", "/programlar"),
        new("Hakkımızda", "/hakkimizda"), new("Ekibimiz", "/ekibimiz"), new("Galeri", "/galeri"), new("İletişim", "/iletisim"),
    ];

    public static SiteSettings Settings() => new()
    {
        SiteName = "Okulum Yeryüzü",
        Tagline = "Orman Okulu ve Doğa Eğitimi",
        FooterText = Tagline,
        Phone = "0542 543 19 93",
        Email = "",
        Address = "Antalya",
        InstagramUrl = "https://instagram.com/okulumyeryuzu",
        HeaderCtaText = "Bize ulaşın",
        HeaderCtaHref = "/iletisim",
        HeaderNav = [.. Nav],
        FooterNav = [.. Nav],
    };

    /// <summary>SEO title/description per built-in page (from the previous site).</summary>
    public static (string Title, string Description) Seo(string slug) => slug switch
    {
        Page.HomeSlug => ("Okulum Yeryüzü | Orman Okulu ve Doğa Eğitimi",
            "Çocukların doğayla bağ kurduğu orman okulu programları, aile kampları ve doğa eğitimi etkinlikleri. Antalya merkezli Okulum Yeryüzü ile tanışın."),
        "hakkimizda" => ("Hakkımızda | Okulum Yeryüzü",
            "Okulum Yeryüzü hakkında; felsefemiz, programlarımız, kurucumuz Pelin Öztepe, profesyonel yaklaşımımız ve iletişim bilgileri."),
        "galeri" => ("Galeri | Okulum Yeryüzü",
            "Orman okulu buluşmalarından, kamp etkinliklerinden ve çocukların doğayla kurduğu bağlardan fotoğraflar. Okulum Yeryüzü galerisi."),
        "ekibimiz" => ("Ekibimiz | Okulum Yeryüzü", "Okulum Yeryüzü orman okulu liderleri ve doğa eğitmenleri."),
        "programlar" => ("Programlar | Okulum Yeryüzü", "Eylül – Haziran dönemini kapsayan düzenli Orman Okulu grupları."),
        "etkinlikler" => ("Kamp ve Etkinlikler | Okulum Yeryüzü", "Aile katılımlı doğa kampları, doğa yürüyüşleri ve atölyeler."),
        "iletisim" => ("İletişim | Okulum Yeryüzü", "Okulum Yeryüzü ile iletişime geçin: telefon, WhatsApp ve Instagram."),
        _ => ("", ""),
    };

    /// <summary>Built-in pages.</summary>
    public static IEnumerable<(string Slug, string Title, List<PageBlock> Blocks)> SystemPages()
    {
        yield return (Page.HomeSlug, "Ana Sayfa",
        [
            B("hero", new
            {
                eyebrow = "Orman okulu ve doğa eğitimi", title = "Çünkü çocukluk dört duvara sığmaz", subtitle = Tagline,
                image = SeedMedia.HeroPhoto,
                primaryText = "Kamp ve etkinlikler", primaryHref = "/etkinlikler",
                secondaryText = "Bizi tanıyın", secondaryHref = "/hakkimizda", layout = "cover",
            }),
            B("imageText", new
            {
                eyebrow = "Biz kimiz", title = "Okulum Yeryüzü",
                body = """
                    Okulum Yeryüzü, çocukların biricikliğine değer veren, doğayla kopan bağlarını yeniden kurmalarına ve içlerindeki cevheri ortaya çıkarmalarına alan açan bir öğrenme topluluğudur.

                    Biz doğayı bir eğitim aracı olarak değil, bir yaşam felsefesi olarak görüyoruz. Çocukların ritimlerine saygı duyduğumuz gibi doğanın ritimlerine de saygı duyuyoruz. Çünkü biliyoruz ki çocuk, doğanın içinde kendi hızında büyür, gelişir ve öğrenir.
                    """,
                image = SeedMedia.CirclePhoto, buttonText = "Hakkımızda", buttonHref = "/hakkimizda",
            }),
            B("features", new
            {
                title = "Orman Okulu Kazanımlarımız",
                intro = "Yapılandırılmamış alan, bütünsel gelişim, süreklilik, faydalı riskler, nitelikli liderler, çocuk merkezlilik, serbest oyun.",
                background = "paper",
                items = new[]
                {
                    new { icon = "leaf", title = "Doğa ile bağ kurma", text = "Çocukların doğa ile iç içe olmaları, sadece çevreyi değil, aynı zamanda kendi iç dünyalarını da zenginleştirir." },
                    new { icon = "heart", title = "Güvenli ayrışma", text = "Bağımsızlık gelişimi desteklenirken, güvenli ayrışma ile ebeveyn ve çocuk arasındaki bağ güçlenir." },
                    new { icon = "compass", title = "Oyun, keşif, macera", text = "Çocuklar oyun, keşif ve macera ile buluşarak yaratıcılıklarını geliştirir ve dünyayı öğrenme arzusuyla büyürler." },
                },
            }),
            B("programs", new
            {
                title = "Etkinliklerimiz", intro = "Sınırlı kontenjanlı kamplarımızda yerinizi ayırtmak için hemen bizimle iletişime geçin.",
                programType = "events", background = "sage", layout = "slider", featuredFirst = true, limit = 12,
                linkText = "Tüm kamp ve etkinlikler", linkHref = "/etkinlikler",
            }),
            B("team", new
            {
                title = "Orman Okulu Liderlerimiz", intro = "Aynı tutkuyu, aynı bilinci taşıyan orman okulu liderlerimiz.",
                limit = 4, layout = "compact", linkText = "Ekibimizle tanışın", linkHref = "/ekibimiz",
            }),
            B("cta", new
            {
                title = "Sorularınız için bize ulaşın",
                text = "Programlarımız hakkında bilgi almak için WhatsApp'tan yazabilir ya da 0542 543 19 93 numaralı telefondan bize ulaşabilirsiniz.",
                buttonText = "WhatsApp'tan yazın", buttonHref = WhatsAppUrl, background = "forest",
            }),
        ]);

        yield return ("hakkimizda", "Hakkımızda",
        [
            B("pageHeader", new
            {
                eyebrow = "Biz kimiz", title = "Hakkımızda",
                intro = "Çünkü çocukluk dört duvara sığmaz. " + Tagline,
            }),
            B("text", new
            {
                title = "Okulum Yeryüzü",
                body = """
                    Okulum Yeryüzü, çocukların biricikliğine değer veren, doğayla kopan bağlarını yeniden kurmalarına ve içlerindeki cevheri ortaya çıkarmalarına alan açan bir öğrenme topluluğudur.

                    Biz doğayı bir eğitim aracı olarak değil, bir yaşam felsefesi olarak görüyoruz. Çocukların ritimlerine saygı duyduğumuz gibi doğanın ritimlerine de saygı duyuyoruz. Çünkü biliyoruz ki çocuk, doğanın içinde kendi hızında büyür, gelişir ve öğrenir.

                    Doğada geçirilen zaman çocukların yalnızca bilgi edinmesini değil; kendini tanımasını, sınırlarını keşfetmesini, özgüven geliştirmesini ve yaşamla gerçek bir bağ kurmasını sağlar. Faydalı risklerle karşılaşan çocuk problem çözmeyi, karar vermeyi, sorumluluk almayı ve zorluklarla baş etmeyi deneyimler. Hayatta kalma becerileriyle gerçek hayatın provasını yapar.

                    **Bizim için doğa öğretilecek bir konu değil, yaşanacak bir deneyimdir.**

                    Bu nedenle serbest oyun yaklaşımımızın merkezindedir. Çocuklara yeterince alan açıldığında kendi keşiflerinin, yetişkinlerin onlara öğretebileceğinden çok daha güçlü ve kalıcı olduğuna inanıyoruz. Öğretmenin görevi yönlendirmekten çok gözlemlemek, eşlik etmek ve çocuğun potansiyelinin ortaya çıkabileceği güvenli ortamı hazırlamaktır.

                    Bugün Okulum Yeryüzü çatısı altında;

                    - Yıl boyu süren düzenli Orman Okulu grupları
                    - Doğa ve ekoloji atölyeleri
                    - Aile katılımlı doğa yürüyüşleri
                    - Aile katılımlı kamplar
                    - Çocuklara yönelik doğa sporları programları
                    - Eğitim kurumlarına danışmanlık hizmetleri
                    - PAN Outdoors markamızla çocuklara yönelik outdoor ürünler

                    sunuyoruz.

                    Hayalimiz; doğayla bağ kuran, kendi gücünü tanıyan, merak eden, üreten ve yaşadığı dünyaya karşı sorumluluk hisseden çocukların çoğalmasıdır.
                    """,
            }),
            B("testimonials", new
            {
                title = "",
                items = new[] { new { quote = "Doğayla bağ kuran, özgüvenli, merak eden ve kendi potansiyelini keşfeden çocuklar için.", author = "Okulum Yeryüzü", role = "" } },
            }),
            B("text", new
            {
                title = "Biz Kimiz?",
                body = """
                    Okulum Yeryüzü, eğitimi destekleyici faaliyetler kapsamında yasal sorumluluklarını yerine getiren resmi bir doğa eğitimi topluluğudur.

                    10 yılı aşkın süredir çocuklar ve ailelerle doğada çalışan Okulum Yeryüzü; orman okulu pedagojisi, çocuk gelişimi, pozitif disiplin, bağ odaklı yaklaşım, doğa temelli öğrenme ve doğada hayatta kalma (bushcraft) yaklaşımlarını bir araya getirerek çocuklara gerçek yaşam deneyimleri sunmaktadır.

                    Çocukların doğayla yeniden bağ kurabilecekleri, özgürce keşfedebilecekleri ve kendi potansiyellerini ortaya çıkarabilecekleri güvenli öğrenme alanları oluşturuyoruz.

                    Amacımız; çocukların doğada güvenle hareket etmelerini, kendi sınırlarını keşfetmelerini, merak etmelerini, üretmelerini, problem çözmelerini ve yaşamla güçlü bir bağ kurmalarını desteklemektir.

                    Biz, çocukların en iyi doğada öğrendiğine; doğanın yalnızca bir etkinlik alanı değil, yaşamın kendisi olduğuna inanıyoruz.
                    """,
            }),
            B("imageText", new
            {
                eyebrow = "Okulum Yeryüzü kurucusu", title = "Pelin Öztepe",
                image = SeedMedia.PelinPhoto, imagePosition = "left",
                body = """
                    Çocuk gelişimi mezunu, doğa temelli eğitim uygulayıcısı ve çocukların doğada öğrenme yolculuğuna eşlik eden bir eğitimciyim.

                    Son 10 yılı aşkın süredir çocuklar, aileler, öğretmenler ve eğitim kurumlarıyla doğada öğrenme çalışmaları yürütüyorum. Bu süreçte çocuk gelişimi alanındaki akademik eğitimimi; orman okulu pedagojisi, doğa temelli öğrenme, bağ odaklı yaklaşımlar, pozitif disiplin, alternatif eğitim modelleri, doğada hayatta kalma becerileri (bushcraft) ve açık hava eğitimi alanlarında aldığım eğitimlerle destekledim.

                    Türkiye'de orman okulu yaklaşımının öncü eğitim grupları arasında yer alan eğitim süreçlerine katıldım. Doğada Öğreniyorum eğitim programının iki aşamasını tamamladım ve Orman Okulu Derneği tarafından verilen eğitmen eğitimlerini aldım. Aynı zamanda bushcraft ve doğada hayatta kalma becerileri alanında eğitimlerimi tamamladım.

                    Lisanslı bir dağcı olarak yıllardır doğada bulunuyor, doğanın yalnızca öğreten değil dönüştüren bir alan olduğuna inanıyorum.
                    """,
            }),
            B("text", new
            {
                title = "",
                body = """
                    Meslek hayatım boyunca birçok okul ve eğitim oluşumunda öğretmen, eğitim koordinatörü, danışman ve eğitmen olarak görev aldım. Öğretmen eğitimleri, doğa temelli eğitim programları ve okul danışmanlıkları yürüttüm. Halen çeşitli eğitim kurumlarıyla iş birliklerim devam etmektedir.

                    Ancak beni bu yolculukta en çok besleyen şey aldığım eğitimlerden çok çocukların kendisi oldu.

                    Yıllar boyunca yüzlerce çocuğu doğada gözlemleme fırsatı buldum. Her defasında aynı gerçeği yeniden gördüm: Çocukların içinde doğal bir merak, öğrenme isteği, yaratıcılık ve yaşam enerjisi var. Biz yetişkinlerin görevi bu cevheri şekillendirmek değil; ortaya çıkabileceği alanları hazırlamak.

                    Okulum Yeryüzü tam da bu anlayışla doğdu. Bugün burada, yıllar içinde edindiğim tüm bilgi, deneyim ve eğitimleri bir araya getirerek geliştirdiğim doğa temelli öğrenme modelini çocuklar ve ailelerle buluşturuyorum.

                    Çünkü inanıyorum ki; çocukların ihtiyacı daha fazla etkinlik değil, daha fazla doğa... Daha fazla yönlendirme değil, daha fazla keşif... Daha fazla öğretim değil, daha fazla deneyimdir.

                    **Ve çocukluk, dört duvara sığmayacak kadar değerlidir.**
                    """,
            }),
            B("text", new
            {
                title = "Neler Yapıyoruz?",
                body = """
                    Okulum Yeryüzü'nde çocuklar, aileler, öğretmenler ve eğitim kurumları için doğa temelli öğrenme deneyimleri tasarlıyoruz.

                    ## Çocuk Programları

                    - Eylül – Haziran dönemini kapsayan düzenli hafta sonu Orman Okulu grupları
                    - 3-4 yaş grubu çocuklara yönelik hafta içi doğa ve orman buluşmaları
                    - Okul gruplarına özel Orman Okulu programları
                    - Ekoloji, doğa keşfi ve doğada yaşam becerileri atölyeleri
                    - Doğada hayatta kalma (bushcraft) eğitimleri ve kampları
                    - Çocuklara yönelik doğa sporları ve açık hava deneyimleri

                    ## Aile Programları

                    - Aile katılımlı doğa yürüyüşleri
                    - Aile katılımlı doğa ve ekoloji atölyeleri
                    - Aile kampları ve doğada birlikte öğrenme deneyimleri
                    - Doğayla bağ kurma ve farkındalık çalışmaları

                    ## Kutlamalar ve Özel Etkinlikler

                    - Ekolojik doğum günü organizasyonları
                    - Orman atölyeleriyle zenginleştirilmiş doğum günü kutlamaları
                    - Doğada tematik etkinlikler ve mevsimsel buluşmalar

                    ## Eğitim Kurumlarına Yönelik Çalışmalar

                    - Okul danışmanlıkları
                    - Öğretmen eğitimleri
                    - Orman Okulu yaklaşımı ve doğa temelli eğitim programları
                    - Risk yönetimi, serbest oyun ve doğa pedagojisi eğitimleri

                    Tüm çalışmalarımızın merkezinde; çocukların doğayla bağ kurması, özgüven geliştirmesi, merakını koruması ve gerçek yaşam deneyimleri yoluyla öğrenmesi yer alır.
                    """,
            }),
            B("text", new
            {
                title = "Profesyonellik ve Sorumluluk",
                body = """
                    Doğada çocuklarla çalışmak, yalnızca doğayı sevmekten ibaret değildir. Çocuğu da doğayı da okuyabilmeyi gerektiren uzmanlık isteyen bir alandır.

                    Bu nedenle Okulum Yeryüzü'nde pedagojik yeterliliğe sahip, çocuk gelişimi ve doğa temelli eğitim alanlarında gerekli eğitimlerini tamamlamış, alanında deneyimli eğitmenlerle çalışıyoruz. Orman okulu pedagojisi, çocuk gelişimi, bağ odaklı yaklaşım, pozitif disiplin, risk yönetimi, açık hava eğitimi ve doğada yaşam becerileri gibi alanlarda uzmanlaşmayı önemsiyoruz.

                    Doğada geçirilen her deneyimin beraberinde sorumluluk getirdiğinin farkındayız. Bu nedenle ekibimizin tüm üyeleri Sağlık Bakanlığı onaylı ilk yardım eğitimlerini tamamlamış olup geçerli ilk yardımcı sertifikaları ve ilk yardımcı kimlik kartlarına sahiptir. Olası riskleri şansa bırakmıyor; düzenli risk analizleri, güvenlik prosedürleri ve acil durum planlamaları ile çalışmalarımızı yürütüyoruz.

                    Çocuklarla doğada çalışırken hem çocuğun gelişimsel ihtiyaçlarını hem de doğanın sürekli değişen koşullarını aynı anda gözetmek gerekir. Bu denge; bilgi, deneyim, dikkat ve sorumluluk gerektirir.

                    Bizim için güvenli öğrenme ortamları oluşturmak yalnızca fiziksel güvenliği sağlamak anlamına gelmez. Çocuğun duygusal güvenliğini, aidiyet hissini, keşif ihtiyacını ve bireysel gelişim ritmini de korumak anlamına gelir.

                    Çocukluk hayatın en değerli dönemlerinden biridir. Bu nedenle hiçbir çocuğun gelişimini şansa bırakmıyor; her deneyimi özenle planlıyor, dikkatle gözlemliyor ve profesyonel bir sorumlulukla yürütüyoruz.

                    Çünkü inanıyoruz ki kaybolan çocukluğun telafisi yoktur. Bu nedenle çocuklara sunduğumuz her deneyimde; özeni, etik sorumluluğu, profesyonelliği ve çocuğun yüksek yararını merkeze alıyoruz.
                    """,
            }),
            B("features", new
            {
                title = "Neden Okulum Yeryüzü?", columns = "2", background = "paper",
                items = new[]
                {
                    new { icon = "mountain", title = "10+ yıllık doğa temelli eğitim deneyimi", text = "" },
                    new { icon = "leaf", title = "Alanından mezun ve pedagojik yeterliliğe sahip eğitmenler", text = "" },
                    new { icon = "tree", title = "Orman okulu ve doğa eğitimi alanında uzmanlaşmış ekip", text = "" },
                    new { icon = "heart", title = "Sağlık Bakanlığı onaylı ilk yardımcı sertifikalarına sahip eğitmenler", text = "" },
                    new { icon = "mountain", title = "Lisanslı dağcılık ve açık hava deneyimine sahip eğitmenler", text = "" },
                    new { icon = "compass", title = "Düzenli risk analizleri ve acil durum eylem planları", text = "" },
                    new { icon = "heart", title = "Çocuk merkezli ve bağ odaklı yaklaşım", text = "" },
                    new { icon = "fire", title = "Faydalı riskleri gözeten güvenli öğrenme ortamları", text = "" },
                    new { icon = "sun", title = "Yıl boyu düzenli ve sürdürülebilir programlar", text = "" },
                    new { icon = "leaf", title = "Resmi, faturalandırılabilir ve yasal sorumluluklarını yerine getiren kurumsal yapı", text = "" },
                },
            }),
            B("text", new
            {
                title = "Çünkü çocukluk değerlidir",
                body = """
                    Çünkü çocukluk kapalı alanlara, ekranlara ve sürekli yönlendirilen programlara sığmayacak kadar değerlidir.

                    Çünkü çocukların yalnızca bilgiye değil; toprağa dokunmaya, ağaca tırmanmaya, yağmurda ıslanmaya, risk almayı öğrenmeye, kendi kararlarını vermeye ve özgürce oyun oynamaya ihtiyaçları vardır.

                    Çünkü her çocuk biriciktir. Her çocuğun gelişim ritmi, ilgi alanları ve öğrenme biçimi farklıdır. Biz çocukları tek bir kalıba sığdırmaya çalışmak yerine onların kendileri olabilecekleri alanlar oluşturuyoruz.

                    Çünkü doğa yalnızca bir etkinlik alanı değil, yaşamın kendisidir. Doğada çocuklar sabretmeyi, çözüm üretmeyi, iş birliği yapmayı, kendilerine güvenmeyi ve yaşamın bir parçası olduklarını deneyimlerler.

                    Çünkü serbest oyun çocuğun en doğal öğrenme dilidir. Biz çocuklara sürekli ne yapmaları gerektiğini söylemek yerine keşfetmeleri, denemeleri, hata yapmaları ve kendi öğrenmelerini inşa etmeleri için alan açıyoruz.

                    Çünkü özgüven övgüyle değil, deneyimle gelişir. Bir ateşi yakabilen, bir barınak kurabilen, bir ağaca tırmanabilen, düştüğünde yeniden deneyebilen çocuk kendi gücünü tanımaya başlar.

                    Çünkü doğayla bağ kuran çocuk yalnızca doğayı korumayı öğrenmez; kendisiyle, diğer insanlarla ve yaşamla da daha güçlü bağlar kurar.

                    Ve çünkü inanıyoruz ki; çocukların ihtiyacı daha fazla etkinlik değil, daha fazla doğa... Daha fazla yönlendirme değil, daha fazla keşif... Daha fazla hız değil, daha fazla oyun... Daha fazla öğretim değil, daha fazla deneyimdir.

                    **Okulum Yeryüzü tam da bu alanı açmak için var.**
                    """,
            }),
            B("text", new
            {
                title = "Bilimsel temeller, profesyonel ekip",
                body = """
                    Okulum Yeryüzü, çocukların doğayla bağ kurmasını destekleyen etkinlikler düzenlemenin ötesinde; bilimsel temellere dayanan, pedagojik olarak planlanmış ve profesyonel ekipler tarafından yürütülen bir doğa eğitimi topluluğudur.

                    10 yılı aşkın saha deneyimimiz boyunca çocukların doğada nasıl öğrendiğini, geliştiğini ve güçlendiğini gözlemledik. Bu deneyimi; çocuk gelişimi, orman okulu pedagojisi, bağ odaklı yaklaşım, pozitif disiplin, açık hava eğitimi ve doğada yaşam becerileri alanlarındaki eğitimlerle bir araya getiriyoruz.

                    Programlarımız rastgele etkinliklerden oluşmaz. Her çalışma; çocukların gelişim özellikleri, ihtiyaçları, ilgi alanları ve güvenlik gereksinimleri dikkate alınarak planlanır. Doğayı bir öğrenme ortamı olarak kullanırken hem çocukların gelişimini hem de doğanın dinamiklerini gözeten bütüncül bir yaklaşım benimseriz.

                    Bizim için güvenlik ve özgürlük birbirinin alternatifi değil, tamamlayıcısıdır. Çocukların faydalı riskleri deneyimleyebilecekleri, aynı zamanda kendilerini fiziksel ve duygusal olarak güvende hissedebilecekleri öğrenme ortamları oluşturuyoruz.

                    Bugün Okulum Yeryüzü'nü tercih eden aileler yalnızca bir etkinliğe değil; deneyim, uzmanlık, pedagojik hassasiyet ve profesyonel sorumlulukla tasarlanmış bir öğrenme ekosistemine dahil olmaktadır.

                    Çünkü çocukluk değerlidir. Bu nedenle her çocuğun karşılaştığı deneyimlerin bilinçle, özenle ve uzmanlıkla tasarlanması gerektiğine inanıyoruz.
                    """,
            }),
            B("cta", new
            {
                title = "Bizi daha yakından tanıyın",
                text = "Okulum Yeryüzü'nü daha yakından tanımak ve programlarımız hakkında bilgi almak için bizimle iletişime geçebilirsiniz.",
                buttonText = "İletişim", buttonHref = "/iletisim", background = "forest",
            }),
        ]);

        yield return ("ekibimiz", "Ekibimiz",
        [
            B("pageHeader", new
            {
                eyebrow = "Orman okulu liderlerimiz", title = "Ekibimiz",
                intro = "Aynı tutkuyu, aynı bilinci taşıyan orman okulu liderlerimiz. Ekibimizin tüm üyeleri alanından mezun, pedagojik yeterliliğe sahip ve Sağlık Bakanlığı onaylı ilk yardımcı sertifikalıdır.",
            }),
            B("team", new { layout = "grid" }),
        ]);

        yield return ("programlar", "Programlar",
        [
            B("pageHeader", new
            {
                eyebrow = "Düzenli orman okulu grupları", title = "Programlar",
                intro = "Eylül – Haziran dönemini kapsayan düzenli Orman Okulu gruplarımız. Kamplarımız ve tek seferlik etkinliklerimiz için Kamp ve Etkinlikler sayfamıza göz atın.",
            }),
            B("programs", new
            {
                programType = "WeeklyClass", layout = "grid", limit = 50, featuredFirst = false, showFilters = true, linkText = "", linkHref = "",
                emptyText = "Yeni programlarımız çok yakında burada olacak. Bilgi almak için bize ulaşabilirsiniz.",
            }),
        ]);

        yield return ("etkinlikler", "Kamp ve Etkinlikler",
        [
            B("pageHeader", new
            {
                eyebrow = "2026 kamp sezonu", title = "Kamp ve Etkinlikler",
                intro = "Aile katılımlı doğa kamplarımız, yürüyüşlerimiz ve atölyelerimiz. Sınırlı kontenjanlı kamplarımızda yerinizi ayırtmak için hemen bizimle iletişime geçin.",
            }),
            B("programs", new
            {
                programType = "events", layout = "grid", limit = 50, featuredFirst = false, linkText = "", linkHref = "",
                emptyText = "Şu an planlanmış bir kamp ya da etkinlik yok. Yakında burada olacak.",
            }),
        ]);

        yield return ("galeri", "Galeri",
        [
            B("pageHeader", new
            {
                eyebrow = "Doğada yaşanan mutluluk anları", title = "Galeri",
                intro = "Orman okulu buluşmalarımızdan, kamp etkinliklerimizden ve çocuklarımızın doğayla kurduğu bağlardan kareler. Her fotoğraf, Okulum Yeryüzü'nün ruhunu taşır.",
            }),
            B("gallery", new
            {
                columns = "3",
                images = SeedMedia.Gallery().Select(url => new { image = url, caption = "" }).ToArray(),
            }),
        ]);

        yield return ("iletisim", "İletişim",
        [
            B("contact", new
            {
                title = "İletişim",
                intro = "Okulum Yeryüzü'nü daha yakından tanımak ve programlarımız hakkında bilgi almak için bizimle iletişime geçebilirsiniz. Telefon ve WhatsApp: 0542 543 19 93",
                workingHours = "", showForm = true, image = SeedMedia.ContactPhoto,
            }),
        ]);
    }
}
