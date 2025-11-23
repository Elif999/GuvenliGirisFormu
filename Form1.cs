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

                            Form2 ana = new Form2();
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

