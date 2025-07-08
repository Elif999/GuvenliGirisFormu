using System;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KullaniciGirisi
{
    public partial class Form1 : Form
    {
        OleDbConnection baglanti;
        OleDbCommand komut;

        string yol = @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\Users\user\ELİF DOSYALARIM\PROJELER\acces veritabanı\kullaniciSifreGrişi.accdb";

        // Brute force için sayaç ve kilit zamanı (Memory'de basit tutuyoruz)
        int hataliDenemeSayisi = 0;
        DateTime kilitZamani = DateTime.MinValue;
        const int maxDeneme = 3;
        readonly TimeSpan kilitSuresi = TimeSpan.FromMinutes(5);

        public Form1()
        {
            InitializeComponent();

            textBox2.PasswordChar = '*';

            this.Load += Form1_Load;
            button1.Click += Button1_Click; // Şifre oluştur
            button2.Click += Button2_Click; // Giriş yap
            button3.Click += Button3_Click; // Şifremi unuttum

            // Telefon numarası alanına her yazı değişiminde buton durumunu kontrol et
            textBox1.TextChanged += textBox1_TextChanged;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Açılışta butonları pasif yap
            ButtonState(false);
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            string tel = textBox1.Text.Trim();

            if (!TelValidMi(tel))
            {
                // Telefon numarası geçerli değilse şifre oluştur aktif, giriş kapalı
                ButtonState(false);
                return;
            }

            using (baglanti = new OleDbConnection(yol))
            {
                try
                {
                    baglanti.Open();
                    string sorgu = "SELECT COUNT(*) FROM şifreler WHERE tel = @tel";
                    komut = new OleDbCommand(sorgu, baglanti);
                    komut.Parameters.AddWithValue("@tel", tel);

                    int kayitSayisi = Convert.ToInt32(komut.ExecuteScalar());

                    if (kayitSayisi > 0)
                    {
                        // Kayıt varsa giriş açık, şifre oluştur kapalı
                        ButtonState(true);
                    }
                    else
                    {
                        // Kayıt yoksa şifre oluştur açık, giriş kapalı
                        ButtonState(false);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Hata: " + ex.Message);
                }
            }
        }

        // Telefon numarası doğrulaması (Türkiye örneği)
        private bool TelValidMi(string tel)
        {
            if (string.IsNullOrEmpty(tel)) return false;
            // Türkiye cep telefonu: 05 ile başlayan 11 haneli
            return Regex.IsMatch(tel, @"^05\d{9}$");
        }

        // Şifre politikası (en az 8 karakter, büyük/küçük harf, rakam, özel karakter)
        private bool SifreGecerliMi(string sifre)
        {
            if (sifre.Length < 8) return false;
            if (!sifre.Any(char.IsUpper)) return false;
            if (!sifre.Any(char.IsLower)) return false;
            if (!sifre.Any(char.IsDigit)) return false;
            if (!sifre.Any(ch => !char.IsLetterOrDigit(ch))) return false;
            return true;
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            string tel = textBox1.Text.Trim();
            string sifre = textBox2.Text.Trim();

            if (!TelValidMi(tel))
            {
                MessageBox.Show("Telefon numarasını 11 haneli ve '05' ile başlayacak şekilde doğru yaz!");
                textBox1.Focus();
                return;
            }

            if (!SifreGecerliMi(sifre))
            {
                MessageBox.Show("Şifre en az 8 karakter olmalı, büyük/küçük harf, rakam ve özel karakter içermeli!");
                textBox2.Focus();
                return;
            }

            using (baglanti = new OleDbConnection(yol))
            {
                try
                {
                    baglanti.Open();

                    // Burada ekstra kontrol: aynı telefon zaten kayıtlı mı diye.
                    string kontrolSorgu = "SELECT COUNT(*) FROM şifreler WHERE tel = @tel";
                    komut = new OleDbCommand(kontrolSorgu, baglanti);
                    komut.Parameters.AddWithValue("@tel", tel);
                    int varMi = Convert.ToInt32(komut.ExecuteScalar());

                    if (varMi > 0)
                    {
                        MessageBox.Show("Bu telefon numarası için zaten kayıt var, şifre oluşturamazsın.");
                        ButtonState(true);
                        return;
                    }

                    string hashlenmisSifre = BCrypt.Net.BCrypt.HashPassword(sifre);

                    string ekleSorgu = "INSERT INTO şifreler(tel, sifre) VALUES (@tel, @sifre)";
                    komut = new OleDbCommand(ekleSorgu, baglanti);
                    komut.Parameters.AddWithValue("@tel", tel);
                    komut.Parameters.AddWithValue("@sifre", hashlenmisSifre);
                    komut.ExecuteNonQuery();

                    MessageBox.Show("Şifren başarıyla oluşturuldu!");
                    ButtonState(true);

                    LogYaz($"{tel} için şifre oluşturuldu.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Hata: " + ex.Message);
                    LogYaz("Şifre oluşturma hatası: " + ex.Message);
                }
            }
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            if (DateTime.Now < kilitZamani)
            {
                MessageBox.Show($"Çok fazla yanlış deneme yaptın. {(kilitZamani - DateTime.Now).Minutes} dakika sonra tekrar dene.");
                return;
            }

            string tel = textBox1.Text.Trim();
            string girilenSifre = textBox2.Text.Trim();

            if (!TelValidMi(tel) || string.IsNullOrEmpty(girilenSifre))
            {
                MessageBox.Show("Telefon ve şifreyi doğru gir!");
                return;
            }

            using (baglanti = new OleDbConnection(yol))
            {
                try
                {
                    baglanti.Open();

                    string sorgu = "SELECT sifre FROM şifreler WHERE tel = @tel";
                    komut = new OleDbCommand(sorgu, baglanti);
                    komut.Parameters.AddWithValue("@tel", tel);

                    object result = komut.ExecuteScalar();
                    if (result != null)
                    {
                        string dbdekiHash = result.ToString();
                        bool dogruMu = BCrypt.Net.BCrypt.Verify(girilenSifre, dbdekiHash);

                        if (dogruMu)
                        {
                            MessageBox.Show("Giriş başarılı!");
                            hataliDenemeSayisi = 0; // Sıfırla
                            LogYaz($"{tel} başarılı giriş yaptı.");

                            Form1 ana = new Form1();
                            ana.Show();
                            this.Hide();
                        }
                        else
                        {
                            hataliDenemeSayisi++;
                            LogYaz($"{tel} yanlış şifre denemesi ({hataliDenemeSayisi}.)");

                            if (hataliDenemeSayisi >= maxDeneme)
                            {
                                kilitZamani = DateTime.Now.Add(kilitSuresi);
                                MessageBox.Show($"3 kez yanlış şifre! Hesap {kilitSuresi.TotalMinutes} dakika kilitlendi.");
                            }
                            else
                            {
                                MessageBox.Show("Şifren yanlış. 'Şifremi unuttum' butonuna bas!");
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Bu telefon numarasına ait hesap bulunamadı.");
                        LogYaz($"{tel} için hesap bulunamadı.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Hata: " + ex.Message);
                    LogYaz("Giriş hatası: " + ex.Message);
                }
            }
        }

        private void Button3_Click(object sender, EventArgs e)
        {
            string tel = textBox1.Text.Trim();

            if (!TelValidMi(tel))
            {
                MessageBox.Show("Telefon numarasını doğru yaz!");
                textBox1.Focus();
                return;
            }

            using (baglanti = new OleDbConnection(yol))
            {
                try
                {
                    baglanti.Open();
                    string silSorgu = "DELETE FROM şifreler WHERE tel = @tel";
                    komut = new OleDbCommand(silSorgu, baglanti);
                    komut.Parameters.AddWithValue("@tel", tel);
                    int silinen = komut.ExecuteNonQuery();

                    if (silinen > 0)
                    {
                        MessageBox.Show("Şifren silindi, yeni şifre oluşturabilirsin.");
                        ButtonState(false);
                        textBox2.Clear();
                        textBox2.Focus();
                        LogYaz($"{tel} için şifre silindi.");
                    }
                    else
                    {
                        MessageBox.Show("Bu telefon numarasıyla kayıt bulunamadı.");
                        LogYaz($"{tel} için silinecek kayıt bulunamadı.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Hata: " + ex.Message);
                    LogYaz("Şifre silme hatası: " + ex.Message);
                }
            }
        }

        private void ButtonState(bool girisVar)
        {
            button1.Enabled = !girisVar;
            button2.Enabled = girisVar;
            button3.Enabled = girisVar;
        }

        // Basit loglama metodu (aynı klasöre log.txt dosyasına yazar)
        private void LogYaz(string mesaj)
        {
            try
            {
                string dosyaYolu = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
                string logMesaj = $"[{DateTime.Now}] {mesaj}";
                File.AppendAllText(dosyaYolu, logMesaj + Environment.NewLine);
            }
            catch
            {
                // Log hatası varsa sessizce geç
            }
        }
    }
}

/*Araştırmalarım:
 * sha-256 ile şifre hashlemenin mantığını anlamak(Ancak projede sha-256 değil benzer ama daha gelişmiş güvenli hashleme kütüphanesini ekledik sha-256 sadece mantığı anlmak içindi)
 
İşte bilgisayarların sayı sistemi, insanların kullandığı sistem, hexadecimal (hex) mantığı ve SHA-256’ya temel hazırlığı içeren **eksiksiz, uzun ve sade tekrar notun**. Bu notu ezberlemen bile gerekmeyecek çünkü içindeki mantık zaten seni yönlendirecek:

---

# 🧠 Bilgisayar Sayı Sistemleri ve Hexadecimal Notu (Uzun, Açıklamalı, Unutulmaz)

---

## 1️⃣ İnsanlar Hangi Sayı Sistemini Kullanır?

### 👨‍👩‍👧‍👦 10 Tabanlı Sistem (Decimal)

* İnsanlar sayıları **10’luk sistemle** sayar. (0–9)
* Bunun sebebi çok basit: **elimizde 10 parmak** var 😄
* Basamaklar:

  * Birler: 10⁰
  * Onlar: 10¹
  * Yüzler: 10²
  * Binler: 10³
  * ...

---

## 2️⃣ Bilgisayarlar Hangi Sistemi Kullanır?

### 💻 2 Tabanlı Sistem (Binary)

* Bilgisayarlar sadece **0 ve 1** ile çalışır. Çünkü temelinde **elektrik vardır**:

  * 1 = elektrik var (açık)
  * 0 = elektrik yok (kapalı)
* Örnek Binary Sayı: `1011`

  * Hesaplama: 1×2³ + 0×2² + 1×2¹ + 1×2⁰ = 8 + 0 + 2 + 1 = **11**

---

## 3️⃣ Binary Çok Uzun Olduğu İçin Ne Kullanılır?

### 💜 16 Tabanlı Sistem (Hexadecimal = Hex)

* Hex, **binary’nin kısa hali** gibi düşünebilirsin.
* 4 bit → 1 Hex karakter

| Binary | Hex |
| ------ | --- |
| 0000   | 0   |
| 0001   | 1   |
| 1010   | A   |
| 1111   | F   |

---

## 4️⃣ Hex Sisteminde Rakamlar:

* 0–9: sayılar
* A–F: harfli değerler

| Hex | Decimal Karşılığı |
| --- | ----------------- |
| A   | 10                |
| B   | 11                |
| C   | 12                |
| D   | 13                |
| E   | 14                |
| F   | 15                |

---

## 5️⃣ Hex Nasıl Hesaplanır? (📌 En çok karıştırılan yer)

### Örnek: `1A3`

| Basamak | Hex Karakter | 16'nın Kuvveti | Sayısal Değeri | Hesaplama     |
| ------- | ------------ | -------------- | -------------- | ------------- |
| 3.      | 1            | 16² = 256      | 1              | 1 × 256 = 256 |
| 2.      | A (10)       | 16¹ = 16       | 10             | 10 × 16 = 160 |
| 1.      | 3            | 16⁰ = 1        | 3              | 3 × 1 = 3     |

**Toplam = 256 + 160 + 3 = 419 (Decimal)** ✅

---

## 6️⃣ Neden Hex Kullanıyoruz?

* Bilgisayarlar binary (ikili) ile çalışır ama binary çok uzun olur.
* Hex, **hem insan için okunabilir**, hem de bilgisayarla uyumlu bir temsildir.
* 1 hex karakter = 4 bit → 64 hex karakter = 256 bit (SHA-256 ile bağlantı!)

---

## 7️⃣ SHA-256 ile Ne Alakası Var?

* SHA-256 bir **kriptografik hash algoritmasıdır**.
* Çıktısı: **256 bit’lik** bir veri verir.
* Bu çıktı genelde **64 karakterlik hexadecimal string** olarak gösterilir.

  > Çünkü 64 hex × 4 bit = 256 bit!

---

## 📌 Not Kutusu: (Kısa Öz Bilgi)

```
📏 İnsanlar → 10’luk sistem (decimal)
🧠 Bilgisayar → 2’lik sistem (binary)
🧰 Binary çok uzun = 101011001...
🎯 Bu yüzden 16’lık sistem (hex) kullanılır = daha kısa & okunabilir
🔐 SHA-256 → 256 bitlik çıktı → 64 karakterlik HEX hash gösterimi
```

---

## 🔁 Mini Egzersizler (Unutma Diye)

| Hex | Hesaplama                  | Decimal |
| --- | ---------------------------| ------- |
| 2F  | 2×16  + 15×1 = 32  +  15   | **47**  |
| 1A  | 1×16  + 10×1 = 16  +  10   | **26**  |
| FF  | 15×16 + 15×1 = 240 +  15   | **255** |
| 10  | 1×16  +  0×1 = 16  +   0   | **16**  |

---

## ✅ Bu Not Ne İşe Yarayacak?

* Renk kodlarını (#FF0000) anlayacaksın.
* MAC adresleri, IP’lerdeki hex gösterimleri çözeceksin.
* En önemlisi: **SHA-256** gibi **güvenlik konularını profesyonel seviyede anlayacaksın.**

---

## 🧱 Sonraki Adımlar:

✅ Sayı sistemleri
🔜 SHA-256’nin bit mantığı (başlıyoruz)
🔜 C# ile SHA256 kodlama
🔜 Salt, pepper ve ileri düzey güvenlik

---

**Yani Hex ≠ sadece bir sayı biçimi değil...**
Hex, bilgisayarlarla insan dili arasında bir **çevirmen köprüsüdür.**

Sen artık bu köprüyü geçtin Aşkım 🚀
Devam edelim mi şimdi SHA-256'nın bit seviyesine?

---

📝 Not Çizgisi:

> "Bilgisayarlar binary sever, insanlar decimal. Hexadecimal ikisi arasında evrensel barış elçisidir." 💻❤️

__________________________________________________________________________________________________________________




Önemli Uyarı:
Aynen öyleee 💡👏
“A” karakterinin karşılığının 10 olması durumu, **iki farklı bağlamda** geçer ama senin kafandaki soru çok doğru yere temas ediyor. Hemen ayırt edelim:

---

## ✅ 1. “A = 10” nerede geçiyor?

Bu **Hexadecimal sistemde** geçerli bir kuraldır.

### 🔢 Hex Sayı Sisteminde:

* Rakamlar 0–9, sonra A–F şeklinde devam eder.
* A = 10, B = 11, … F = 15

> Bu sadece **sayı sistemidir**, yani matematiksel bir gösterim. ASCII ile doğrudan alakası **yoktur**.

### Örnek:

`1A` hex sayısı →
1 × 16¹ + A(10) × 16⁰ = 16 + 10 = 26 (decimal)

---

## 🔠 2. Peki ASCII Nedir? A’nın karşılığı kaç?

**ASCII (American Standard Code for Information Interchange)**,
bilgisayarların harfleri, sembolleri ve karakterleri sayıya çevirmek için kullandığı bir standarttır.

| Karakter | ASCII Kodu |
| -------- | ---------- |
| A        | 65         |
| B        | 66         |
| a        | 97         |
| 0        | 48         |
| !        | 33         |

👉 Yani **ASCII’de "A" karakteri = 65**,
ama **Hex’te "A" rakamı = 10**

---

## 🤔 Karışmaması için şöyle hatırla:

| Sistem      | “A” ne anlama gelir? |
| ----------- | -------------------- |
| Hexadecimal | 10 (sayı değeri)     |
| ASCII       | 65 (karakter kodu)   |

---

## 📌 Not Kutusu:

```
🔢 Hex sisteminde A = 10’dur, çünkü 0-9’dan sonra A-F gelir (A=10, B=11, … F=15)
🔠 ASCII’de A = 65’tir çünkü A harfi bir karakterdir ve her karakterin sayısal kodu vardır
❗ Hex = Sayı sistemi
❗ ASCII = Karakter kodlama sistemi
```

---

İkisi tamamen farklı alanlar ama C#’ta bazen bu ikisini birlikte kullandığımız olur — mesela:

```csharp
char harf = 'A';
int asciiKodu = (int)harf;  // Çıktı: 65
```

İstersen bu farkı test etmek için mini bir kod bile yazarız 🔥
Hazırsan, "ASCII ile ilgili de pratik yapalım" de yeter 💻💬

---

📝 Not Çizgisi:

> "Hex, sayının kısayolu. ASCII, harfin dili. Karıştırma, kralsın!" 👑💻


_________________________________________________________________________________________________



Soru:
7. Ufak Egzersiz: Hex “2F” kaç decimal?
2 × 16¹ = 2 × 16 = 32

F = 15 × 16⁰ = 15 × 1 = 15

Toplam = 32 + 15 = 47
=>
Hiç sorun değil ! 🧠
Seninle birlikte **hexadecimal → decimal dönüşümünü** bir kez daha adım adım yapalım. Hem eğlenceli hem sade olacak, bu sefer %100 oturacak. ✨

---

## ✅ Soru:

Hexadecimal sayı: `2F`

> Bu sayı **16 tabanında**, yani her basamak **16’nın kuvvetlerine göre** değerlendirilir.

---

## 🔍 Adım Adım Çözüm:

### 🔢 Adım 1: Basamakları yazalım (sağdan sola)

| Basamak               | Karakter | 16'nın Kuvveti | Sayısal Değeri | Hesaplama   |
| --------------------- | -------- | -------------- | -------------- | ----------- |
| Sağdan 1 (en sağdaki) | F        | 16⁰ = 1        | 15             | 15 × 1 = 15 |
| Sağdan 2 (en soldaki) | 2        | 16¹ = 16       | 2              | 2 × 16 = 32 |

---

### ➕ Adım 2: Toplayalım

```
32 + 15 = 47
```

---

## ✅ Sonuç:

**Hex "2F" = Decimal "47"**

🎉 İşte bu kadar!
Senin çözmende zorlanman çok normal çünkü başta alışması zaman alır ama artık çok daha iyi anladın değil mi?

---

## 💡 Kısa Kural (Ezber için):

* Her **hex basamağı** = 16’nin bir kuvvetiyle çarpılır
* **0–9** = zaten aynısı
* **A–F** = 10–15

---

İstersen şimdi sana birkaç küçük hex egzersizi vereyim, çözmek ister misin? 🤓
Ya da SHA-256’ya geçelim mi? ⛓️💻
Sen seç, ben buradayım!
SayiSistemleri_SHA256_SoruCevaplar.pdf //edge arama kutusuna yaz
_______________________________________________________________________________________________________


 temelden başlıyoruz, rahat ol! 🚀

---

## SHA-256 Hash’in İç Yapısı ve Formatı – Baştan Sona Anlatım

---

### 1. SHA-256 Nedir?

* **SHA** = Secure Hash Algorithm (Güvenli Özetleme Algoritması)
* SHA-256, SHA-2 ailesinin en çok kullanılan üyesi.
* **256**: Çıktısının 256 bit (yani 32 byte) uzunluğunda olduğunu gösterir.
* Amacı: Herhangi bir veriyi (örneğin bir şifreyi) alır, tek yönlü (geri dönüştürülemez) sabit uzunlukta bir “özet” (hash) oluşturur.

---

### 2. Bit ve Byte Kavramları

* **1 bit** = 0 ya da 1 olabilir (binary sistemin en küçük birimi).
* **1 byte** = 8 bit.
* SHA-256 çıktısı **256 bit**, yani **256 / 8 = 32 byte** uzunluğundadır.

---

### 3. SHA-256 Hash’in Formatı

* Çıktı, bilgisayar içinde **binary (0-1 dizisi)** olarak tutulur.
* Bu 256 bitlik binary değer, insanın okuyabilmesi için genellikle **hexadecimal (16’lık) sayı sisteminde** gösterilir.
* 1 hex karakter = 4 bit → 256 bit / 4 bit = **64 hex karakteri** olur.

**Yani**:
SHA-256 hash çıktısı çoğunlukla **64 karakterlik bir hex string** olarak görünür.

---

### 4. Örnek SHA-256 Hash

Mesela, “hello” kelimesinin SHA-256 hash’i şöyle bir şeydir:

```
2CF24DBA5FB0A30E26E83B2AC5BCD9F1...
(Toplam 64 karakter)
```

Her karakter 0-9, A-F arasında, hexadecimal sistemde.

---

### 5. Hash’in Özellikleri

* Aynı girdiye her zaman aynı hash değerini üretir.
* Girdi çok ufak değişse bile hash tamamen farklı olur (avalanche etkisi).
* Hash’ten orijinal veriye dönmek imkansızdır (tek yönlü fonksiyon).

---

### 6. SHA-256’nın Çalışma Mantığı (Kısaca)

* Girdi (örneğin şifre) önce binary’ye çevrilir.
* Girdi belirli boyutta bloklara bölünür (512 bitlik bloklar).
* Karmaşık matematiksel işlemlerle bu bloklar işlenir.
* Sonunda 256 bitlik özet (hash) üretilir.

---

### 7. Neden SHA-256?

* Güvenli: Çözülemez, geri döndürülemez.
* Sabit uzunluk: İster 1 byte, ister 1 GB veri olsun, hash hep 256 bit.
* Yaygın: Günümüz sistemlerinde en çok tercih edilen hash algoritması.

---

### 8. Senin Projende Nasıl İşler?

* Kullanıcı şifreyi girer → Şifre SHA-256 ile hashlenir (hash fonksiyonundan geçirilir).
* Veritabanında hashlenmiş hali saklanır.
* Girişte tekrar kullanıcı şifreyi girer → Aynı hash fonksiyonuna sokulur.
* Oluşan hash, veritabanındaki hash ile karşılaştırılır.
* Eğer aynıysa şifre doğru kabul edilir.

---

### 9. Özet ve Sonraki Adım

| Konu              | Özet                                         |
| ----------------- | -------------------------------------------- |
| SHA-256           | Sabit 256 bitlik tek yönlü hash fonksiyonu   |
| Hash Formatı      | Genelde 64 karakterlik hexadecimal string    |
| Bit-Hex İlişkisi  | 1 hex karakter = 4 bit                       |
| Projede Kullanımı | Şifreyi hashleyip, doğrulamada karşılaştırma |

---

Bir sonraki adımda SHA-256’yı C# ile nasıl kullanacağını, kodunu beraber yazıp test edeceğiz.

---

Bu temel mantıkta kafan rahat mı?
Anlamadığın veya detay istediğin kısım varsa hemen sor, sana uygun örneklerle açayım!

______________________________________________________________________________________________________

Bu son geliştirmeler ama daha fazla geliştirme eklenebilir:

Kodun gayet iyi görünüyor, hem bcrypt ile hashliyor hem de temel validasyon var. Ama tam bir **güvenlik zırhı** için aşağıdaki adımları da mutlaka düşün:

---

### 1. **Telefon Numarası Doğrulamasını Güçlendir**

* Şu an sadece 10-11 hane ve rakam kontrolü var.
* Regex ile daha katı kontrol yapabilirsin. Mesela Türkiye telefon numarası formatına göre doğrulama:

```csharp
using System.Text.RegularExpressions;

private bool TelValidMi(string tel)
{
    return Regex.IsMatch(tel, @"^(05\d{9}|5\d{9})$"); // Örnek: 05XXXXXXXXX veya 5XXXXXXXXX
}
```

**Ama uygulamana göre uyarlayabilirsin.**

---

### 2. **SQL Injection'a Karşı Parametre Kullanımı**

* Zaten sen parametre kullanıyorsun, bu çok iyi. `AddWithValue` kullanımı SQL injection’ı engeller.

---

### 3. **Şifre Politikası**

* Şifrelerin kolay olmamasını sağlamalısın (en az 8 karakter, büyük/küçük harf, rakam, özel karakter gibi).
* Örnek:

```csharp
private bool SifreGecerliMi(string sifre)
{
    if (sifre.Length < 8) return false;
    if (!sifre.Any(char.IsUpper)) return false;
    if (!sifre.Any(char.IsLower)) return false;
    if (!sifre.Any(char.IsDigit)) return false;
    if (!sifre.Any(ch => !char.IsLetterOrDigit(ch))) return false;
    return true;
}
```

Ve şifre oluştururken bu fonksiyonu kullan.

---

### 4. **Brute Force Saldırılarını Önle**

* Aynı telefon numarası ile çok fazla yanlış şifre denemesinde kullanıcıyı kilitle veya beklet.
* Örneğin 3 başarısız denemeden sonra 5 dakika beklet.

---

### 5. **Veri Tabanı Şifreleme**

* Access zaten küçük projeler için uygun, ama kritik veriler varsa veritabanı dosyasını şifrelemeyi düşünebilirsin.

---

### 6. **TLS / SSL Kullanımı**

* Uygulaman internet üzerinden iletişim kuruyorsa, verinin şifreli gitmesini sağla (örneğin HTTPS, VPN).

---

### 7. **Şifre Sıfırlama İşlemi**

* "Şifremi unuttum" butonu sadece şifreyi veritabanından siliyor, daha iyi yol:

  * Kullanıcıya mail veya SMS ile şifre sıfırlama linki gönder.
  * Sadece telefon silmek yerine doğrulama sonrası şifre değiştirmeyi sağla.

---

### 8. **Logging ve İzleme**

* Başarılı ve başarısız girişleri logla.
* Anormal durumları takip et.

---

### 9. **Güncel Kütüphane Kullanımı**

* `BCrypt.Net` gibi kütüphaneleri güncel tut.

---

# Özet

> Şu anki kodun **temel bir güvenlik seviyesi** sağlıyor,
> ama yukarıdaki ek önlemlerle **profesyonel seviyeye** taşıyabilirsin.

---

İstersen yukarıdakilerden hangisine öncelik vermen gerektiğini söylerim, hatta kodunu da ona göre optimize ederim.
Bana göre ilk 3 madde (telefon doğrulama, şifre politikası, brute force koruması) şart!

Nasıl devam edelim?

___________________________________________________________________________________________________________________

Süper, şimdi bu kritik kısma yani **veritabanına doğrudan bağlantı yerine API kullanmak ve güvenlik önlemleri** konusuna basit, anlaşılır şekilde bakalım. 👇

---

## 1. Veritabanına Neden Doğrudan Bağlanılmaz?

* Eğer uygulaman doğrudan veritabanına (SQL Server’a) internet üzerinden bağlanırsa:

  * Veritabanı **açık hedef** olur, hacker’lar kolayca saldırabilir.
  * Bağlantı bilgileri (kullanıcı adı, parola) uygulama içinde açığa çıkabilir.
  * Sorgular kötü niyetli kişilerce manipüle edilebilir (SQL Injection riski artar).
* Çok kullanıcılı ve güvenli bir sistemde, **ara katman** kullanılır: API (Application Programming Interface).

---

## 2. API Nedir?

* API, sunucuda çalışan ve istemciden gelen istekleri karşılayan program parçasıdır.
* İstemci (web, mobil, masaüstü uygulama) API’ye HTTP üzerinden istek yapar.
* API, bu istekleri işleyip veritabanına güvenli şekilde yönlendirir, verileri alır ve yanıt verir.

---

## 3. API Kullanmanın Avantajları

* **Güvenlik:** Doğrudan veritabanına erişim engellenir.
* **Yetkilendirme:** Kimlerin neye erişebileceği kontrol edilir.
* **Veri Doğrulama:** API gelen verileri kontrol eder, saldırıları engeller.
* **Kolay Yönetim:** İş kuralları API’de tutulur, değişiklikler merkezi olur.

---

## 4. API Nasıl Yapılır?

* **Teknolojiler:** ASP.NET Core Web API, Node.js Express, Django REST Framework vb.
* **İstemci:** Windows Form uygulaman API’ye HTTP istekleri gönderir.
* **Veritabanı:** API, SQL Server’a bağlanır ve sorguları yapar.

---

## 5. Güvenlik Önlemleri API ve Veritabanı İçin

| Önlem                          | Ne İşe Yarar?                               | Nasıl Yapılır?                     |
| ------------------------------ | ------------------------------------------- | ---------------------------------- |
| **Güçlü Şifreler**             | Yetkisiz erişimi engeller                   | Karmaşık parola ve hash kullan     |
| **IP Kısıtlamaları**           | Sadece izin verilen IP’ler bağlanabilir     | Firewall, SQL Server ayarları      |
| **SSL (HTTPS)**                | Veri transferini şifreler                   | Web sunucusuna SSL sertifikası kur |
| **JWT Token**                  | Kullanıcı kimlik doğrulama ve yetkilendirme | API isteklerinde token doğrulama   |
| **Rate Limiting**              | Saldırıları sınırlar                        | API Gateway veya middleware        |
| **Veri Doğrulama & Temizleme** | Zararlı veri girişini engeller              | API tarafında validasyon yap       |
| **Parametreli Sorgular**       | SQL Injection riskini azaltır               | SQL sorgularında parametre kullan  |

---

## 6. Özet

* Windows Forms uygulaman doğrudan SQL Server’a bağlanmaz.
* Onun yerine API üzerinden **HTTPS istekleriyle** konuşur.
* API, kullanıcı girişlerini kontrol eder, veritabanıyla güvenli iletişim kurar.
* Böylece veri daha güvenli ve kontrol altında olur.

---

## İstersen Başlangıç İçin Basit ASP.NET Core Web API Örneği ve Form’dan API’ye Bağlanma kodu da verebilirim.

Sen nasıl istersen, adım adım ilerleriz. 🚀
_________________________________________________________________________________________________________
Süper, şimdi bu kritik kısma yani **veritabanına doğrudan bağlantı yerine API kullanmak ve güvenlik önlemleri** konusuna basit, anlaşılır şekilde bakalım. 👇

---

## 1. Veritabanına Neden Doğrudan Bağlanılmaz?

* Eğer uygulaman doğrudan veritabanına (SQL Server’a) internet üzerinden bağlanırsa:

  * Veritabanı **açık hedef** olur, hacker’lar kolayca saldırabilir.
  * Bağlantı bilgileri (kullanıcı adı, parola) uygulama içinde açığa çıkabilir.
  * Sorgular kötü niyetli kişilerce manipüle edilebilir (SQL Injection riski artar).
* Çok kullanıcılı ve güvenli bir sistemde, **ara katman** kullanılır: API (Application Programming Interface).

---

## 2. API Nedir?

* API, sunucuda çalışan ve istemciden gelen istekleri karşılayan program parçasıdır.
* İstemci (web, mobil, masaüstü uygulama) API’ye HTTP üzerinden istek yapar.
* API, bu istekleri işleyip veritabanına güvenli şekilde yönlendirir, verileri alır ve yanıt verir.

---

## 3. API Kullanmanın Avantajları

* **Güvenlik:** Doğrudan veritabanına erişim engellenir.
* **Yetkilendirme:** Kimlerin neye erişebileceği kontrol edilir.
* **Veri Doğrulama:** API gelen verileri kontrol eder, saldırıları engeller.
* **Kolay Yönetim:** İş kuralları API’de tutulur, değişiklikler merkezi olur.

---

## 4. API Nasıl Yapılır?

* **Teknolojiler:** ASP.NET Core Web API, Node.js Express, Django REST Framework vb.
* **İstemci:** Windows Form uygulaman API’ye HTTP istekleri gönderir.
* **Veritabanı:** API, SQL Server’a bağlanır ve sorguları yapar.

---

## 5. Güvenlik Önlemleri API ve Veritabanı İçin

| Önlem                          | Ne İşe Yarar?                               | Nasıl Yapılır?                     |
| ------------------------------ | ------------------------------------------- | ---------------------------------- |
| **Güçlü Şifreler**             | Yetkisiz erişimi engeller                   | Karmaşık parola ve hash kullan     |
| **IP Kısıtlamaları**           | Sadece izin verilen IP’ler bağlanabilir     | Firewall, SQL Server ayarları      |
| **SSL (HTTPS)**                | Veri transferini şifreler                   | Web sunucusuna SSL sertifikası kur |
| **JWT Token**                  | Kullanıcı kimlik doğrulama ve yetkilendirme | API isteklerinde token doğrulama   |
| **Rate Limiting**              | Saldırıları sınırlar                        | API Gateway veya middleware        |
| **Veri Doğrulama & Temizleme** | Zararlı veri girişini engeller              | API tarafında validasyon yap       |
| **Parametreli Sorgular**       | SQL Injection riskini azaltır               | SQL sorgularında parametre kullan  |

---

## 6. Özet

* Windows Forms uygulaman doğrudan SQL Server’a bağlanmaz.
* Onun yerine API üzerinden **HTTPS istekleriyle** konuşur.
* API, kullanıcı girişlerini kontrol eder, veritabanıyla güvenli iletişim kurar.
* Böylece veri daha güvenli ve kontrol altında olur.

---

## İstersen Başlangıç İçin Basit ASP.NET Core Web API Örneği ve Form’dan API’ye Bağlanma kodu da verebilirim.

Sen nasıl istersen, adım adım ilerleriz. 🚀

___________________________________________________________________________________________________________________________



eğer kullanıcı olmayan bir telefon numarasıyla kayıt olmaya çalışırsa bu çok kötü ve saçma olur nasıl engellleyeceğiz:


Kesinlikle haklısın, “kullanıcı olmayan telefon numarasıyla kayıt olma” durumu sistemde kötüye kullanımı artırabilir, spam veya sahte hesaplar oluşabilir. Bu yüzden telefona gerçeklik doğrulaması yapmak şart.

Bunu engellemenin en mantıklı ve yaygın yolu:

1. Telefon Doğrulama (SMS Doğrulama) Adımı Ekle
Kullanıcı şifre oluşturma (kayıt) istediğinde önce telefon numarasına SMS ile tek kullanımlık doğrulama kodu (OTP) gönderilir.

Kullanıcı o kodu uygulamada girer, doğrulanırsa kayıt işlemi devam eder.

Eğer doğrulanmazsa kayıt yapılmaz.

2. API ve SMS Servisi Kullanımı
Bunun için bir SMS API sağlayıcı (örn. Twilio, Nexmo, Türk Telekom SMS API vs.) kullanılır.

Backend tarafında telefon numarasına OTP gönderilir ve kontrol edilir.

3. Kullanıcı Arayüzü (Form) Mantığı
“Şifre oluştur” butonuna basınca önce telefon numarasını doğrula (SMS gönder).

Kullanıcı doğrulama kodunu girince “kayıt tamamla” yapılır.

Doğrulanmamış numara ile şifre oluşturma engellenir.

Kısaca Pratik Öneri
Mevcut C# Windows Form uygulamanı biraz değiştirip, “Telefonu Doğrula” adımı ekleyebilirsin.

Önce telefon numarasına SMS gönder, doğrulama kodu al, kullanıcıdan kodu iste.

Kod doğruysa kayıt işlemini yap.

Senin İçin Hızlı Mantık:

// 1. Telefon doğrulama için buton ve textbox ekle: txtOtp, btnSendOtp, btnVerifyOtp

// 2. btnSendOtp_Click => SMS gönder, 6 haneli rastgele kod üret ve backend'e sakla

// 3. btnVerifyOtp_Click => Girilen kod backend'deki ile karşılaştır, doğruysa kayıt yapmaya izin ver

// 4. Şifre oluşturma kısmında sadece doğrulama geçerse kayıt yap
Özet
Telefon numarasını doğrulamadan kayıt almak çok riskli ve kullanışsızdır.

SMS OTP doğrulaması ile hem gerçek kullanıcı hem de spam önlenir.

Bu doğrulama mutlaka internet ve backend üzerinden SMS API ile yapılır.

Eğer istersen, sana bu SMS doğrulama sistemi için örnek mimari ve kod parçacıkları da verebilirim. Elektron/Node.js, C# veya başka platform fark etmez.

Öyleyse söyle, doğrulama kodu sistemiyle devam edelim mi? 🚀
*/

