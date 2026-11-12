# Demo verisi (App Store / Google Play incelemesi)

Apple ve Google inceleyicileri uygulamaya giriş yapabilmek için hesap istiyor. Gerçek çocuk ve veli verisi
görmemeleri için kendi içinde kapalı, tamamen sahte bir "demo dünyası" kuruldu. Hepsi tek bir migration'da:
`src/Oy.Api/Data/Migrations/20261111230450_DemoData.cs`. API açılırken otomatik uygulanır, ayrıca bir şey
yapmak gerekmez.

## Hesaplar

| Rol | E-posta | Şifre | Ne görür? |
|---|---|---|---|
| Veli | `demo.veli@okulumyeryuzu.com` | `OyDemo-bjntUkflAF!7` | Yalnızca hayali çocuğu **Deniz Demo**: akış, fotoğraflar, bildirimler, Hesabım |
| Öğretmen | `demo.ogretmen@okulumyeryuzu.com` | `OyDemo-OI4CBaM3v8!7` | Yalnızca **Demo Grup**'un dersleri (Sınıf ve yoklama), kendi işleri, bildirimler, Hesabım |

- Öğretmenin rolü **"Öğretmen (demo)"**. Tek yetkisi `classroom.use`. Kayıtlar, eğitimler, finans, personel ve
  diğer eğitimlerin dersleri bu hesaptan **görünmez**; API 403 döner. Yerelde test edildi.
- Veli yalnızca kendi çocuğunu ve **Demo Grup**'a gönderilen paylaşımları görür. Panelden **"Tüm veliler"**
  hedefiyle yapılan genel duyurular da gider. İnceleme sürerken içinde çocuk fotoğrafı olan genel duyuru
  paylaşmayın, ilgili eğitime hedefleyin.
- E-posta adresleri gerçek posta kutusu değil, giriş adı olarak kullanılıyor. Sistem bu adreslere e-posta göndermez.
- Şifreler bu dosyada ve migration'da (hash olarak) duruyor. Repo gizli olduğu ve hesaplar yalnızca sahte veriye
  eriştiği için kabul edilebilir. Şifre değiştirilirse bu tabloyu ve mağazalardaki inceleme notlarını da güncelleyin.

## Demo içeriği

- **Eğitim:** "Demo Grup – Uygulama İnceleme" (slug `demo-uygulama-inceleme`).
  - **Yayında değil**, sitede görünmez.
  - Ücret 0 olduğu için ödeme takibine ve finansa girmez.
  - Ön kayda kapalı. Konum: "Demo Orman Alanı", 5–8 yaş.
  - Migration'ın çalıştığı günden 14 gün öncesinden 1 yıl sonrasına kadar **her gün 10:00–12:00 ders**. İnceleyici
    hangi gün bakarsa baksın Sınıf ekranında bugünün dersi olsun diye her gün.
- **Personel:** "Demo Öğretmen" (sitede gösterilmez, ders başı ücret 0). Demo gruba atanmış.
- **Çocuklar (hepsi hayali, Aktif):** Deniz Demo (velisi demo veli), Ege Demo, Lina Demo.
- **Geçmiş 3 ders:** başlatılmış ve bitirilmiş, yoklama alınmış (Lina bir gün gelmemiş). Gözlem notları:
  Deniz için 2, Ege için 1.
- **Veli akışı:** demo gruba 3 paylaşım (1, 3 ve 8 gün önce).
- **Öğretmenin işleri:** 3 iş (biri bugün son gün, biri önemli).
- **Bildirimler:** iki hesapta da birer okunmamış bildirim (zil boş görünmesin).
- **Fotoğraf yok.** Dosya gerektirdiği için migration'a konmadı. İsterseniz demo öğretmenle giriş yapıp bugünün
  dersinde **gerçek çocuk içermeyen** doğa fotoğrafları yükleyin ve Deniz'i etiketleyin. Veli Fotoğraflar
  sekmesinde görür. Ekran görüntüleri için önerilir.

Kayıtların sabit kimlikleri `d3a00000-0000-4000-8000-0000000000NN` biçiminde: 01 eğitim, 02 personel,
03 atama, 04 rol, 05 öğretmen kullanıcısı, 06 veli kullanıcısı, 11–13 kayıtlar, 21 veli bağlantısı,
31–33 paylaşımlar, 41–43 işler, 51–52 bildirimler. Migration `ON CONFLICT DO NOTHING` kullanır; tekrar çalışsa
bile çift kayıt oluşmaz.

## Panelde görünecekler (bilinmesi gerekenler)

Yönetici olarak panelde şunlar görünür:
- Eğitimler listesinde ve **Takvim**'de her gün bir "Demo Grup" dersi,
- **Pano**'da "Önümüzdeki 7 gün" bölümünde demo dersleri,
- aktif eğitim ve aktif kayıt sayılarında +1 eğitim ve +3 kayıt.

Kayıtlar listesinde "Demo Grup – Uygulama İnceleme" filtresiyle demo kayıtlarını ayırabilirsiniz.
Finans ve ödeme takibi etkilenmez.

## Mağaza inceleme notları

### App Store Connect → App Review Information
- Sign-in required: ✔
- User name: `demo.veli@okulumyeryuzu.com`, Password: `OyDemo-bjntUkflAF!7`
- Notes (İngilizce):

```
Okulum Yeryüzü is the private app of a nature school (forest school) in Antalya, Turkey. It is used only by the
families of enrolled children and by the school's staff; accounts are created by the school, there is no public
sign-up. The app is published by Ali Öztepe on behalf of the business owner, Pelin Öztepe (Okulum Yeryüzü).
Privacy policy: https://okulumyeryuzu.com/gizlilik

All data in the demo accounts is fictional (demo group, children "Deniz Demo", "Ege Demo", "Lina Demo").

Parent account (the main use of the app):
  demo.veli@okulumyeryuzu.com / OyDemo-bjntUkflAF!7
  Feed with announcements, the child's photos, notifications, account page (Hesabım) with
  privacy policy and account deletion.

Teacher account:
  demo.ogretmen@okulumyeryuzu.com / OyDemo-OI4CBaM3v8!7
  Classroom (Sınıf): start today's lesson, take attendance, write observation notes, share photos with the
  children's parents; to-dos (İşler); notifications.

Account deletion: Hesabım → "Hesabımı sil" (https://okulumyeryuzu.com/hesap-silme).
```

### Google Play Console → App content → App access
"All or some functionality is restricted" → **Add instructions**:
- Name: `Demo parent`, user name `demo.veli@okulumyeryuzu.com`, password `OyDemo-bjntUkflAF!7`
- Name: `Demo teacher`, user name `demo.ogretmen@okulumyeryuzu.com`, password `OyDemo-OI4CBaM3v8!7`
- Other information: yukarıdaki notun ilk iki paragrafı.

## Demo verisini kaldırmak

Mağazalar sonraki güncellemelerde de hesaplara tekrar bakabildiği için onaydan sonra **silinmemesi önerilir**.
Gerekirse:

- **Geçici:** Panel → Kullanıcılar'da iki demo hesabı pasife alın. Takvim kalabalığı rahatsız ederse demo
  eğitimin bitiş tarihini bugüne çekin.
- **Tamamen:** Migration'ın `Down` kısmı her şeyi (dersler, yoklama, notlar, inceleyicinin yüklediği fotoğraflar,
  kayıtlar, hesaplar, rol, personel) sırasıyla siler. Yerelde aşağı/yukarı denendi. Sunucuda çalıştırmak yerine
  tercihen aynı SQL'i elle çalıştırın, çünkü migration'ı geri almak sonraki migration'ları da geri alır.
  Fotoğraf dosyaları depoda kalır; yalnızca kayıtları silinir.
