using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Data;

/// <summary>
/// Baslangic sablonlari. Govdedeki yer tutucular EmailTemplateService tarafindan doldurulur.
/// Key alani AI'in onerdigi sablon anahtari ile eslesir.
/// DIKKAT: Kullanici bu taslaklari Taslak Duzenleyici'den degistirebilir. Buradaki
/// degerleri degistirmek yeni bir migration'da UpdateData uretir ve kullanicinin
/// duzenlemelerinin uzerine yazar; yeni taslaklar icin burayi degil ekrani kullanin.
/// </summary>
public static class EmailTemplateSeed
{
    public static readonly EmailTemplate[] All =
    {
        new()
        {
            Id = 1,
            Key = "SAP",
            Name = "SAP Danışmanlığı",
            Subject = "Üretim süreçleri ve SAP danışmanlığı hakkında",
            Body = """
                Sayın {CONTACT_NAME},

                {COMPANY_NAME} firmasının {INDUSTRY} alanındaki faaliyetlerini inceledik.

                Egebis olarak üretim şirketlerine yönelik SAP danışmanlığı,
                entegrasyon ve özel yazılım çözümleri geliştiriyoruz.

                Firmanızın mevcut yapısı ile ilgili kısa bir görüşme yaparak
                karşılıklı olarak değerlendirebileceğimiz alanlar olup olmadığını
                konuşmak isteriz.

                Uygun olduğunuz bir zamanda 20 dakikalık bir görüşme
                gerçekleştirmekten memnuniyet duyarız.

                Saygılarımızla,
                Egebis Bilişim
                """
        },
        new()
        {
            Id = 2,
            Key = "SAP_ENTEGRASYON",
            Name = "SAP Entegrasyonu",
            Subject = "SAP entegrasyonu ve sistem bütünleştirme hakkında",
            Body = """
                Sayın {CONTACT_NAME},

                {COMPANY_NAME} firmasının {INDUSTRY} alanındaki üretim faaliyetlerini inceledik.

                Egebis olarak SAP ile mevcut sistemleriniz (üretim hattı, depo,
                e-ticaret, muhasebe) arasındaki entegrasyonları kuruyor,
                veri akışını uçtan uca otomatikleştiriyoruz.

                Mevcut entegrasyon ihtiyaçlarınızı değerlendirmek üzere
                20 dakikalık kısa bir görüşme yapmak isteriz.

                Saygılarımızla,
                Egebis Bilişim
                """
        },
        new()
        {
            Id = 3,
            Key = "URETIM_YAZILIMI",
            Name = "Üretim Yazılımı",
            Subject = "Üretim süreçlerinize özel yazılım çözümleri",
            Body = """
                Sayın {CONTACT_NAME},

                {COMPANY_NAME} firmasının {INDUSTRY} alanındaki çalışmalarını inceledik.

                Egebis olarak üretim yapan firmalara özel; iş emri takibi,
                kalite kontrol ve raporlama yazılımları geliştiriyoruz.

                Süreçlerinizde yazılımla iyileştirilebilecek alanları
                birlikte değerlendirmek isteriz.

                Saygılarımızla,
                Egebis Bilişim
                """
        },
        new()
        {
            Id = 4,
            Key = "MES",
            Name = "MES / Üretim Takip",
            Subject = "Üretim takip (MES) sistemleri hakkında",
            Body = """
                Sayın {CONTACT_NAME},

                {COMPANY_NAME} firmasının {INDUSTRY} alanındaki üretim kapasitesini inceledik.

                Egebis olarak sahadan gerçek zamanlı veri toplayan MES /
                üretim takip sistemleri kuruyoruz: makine verimliliği (OEE),
                duruş analizi ve anlık üretim raporlaması.

                Mevcut üretim takip yapınızı konuşmak üzere kısa bir
                görüşme yapmaktan memnuniyet duyarız.

                Saygılarımızla,
                Egebis Bilişim
                """
        },
        new()
        {
            Id = 5,
            Key = "GENEL",
            Name = "Genel Tanıtım",
            Subject = "Egebis Bilişim - kurumsal yazılım çözümleri",
            Body = """
                Sayın {CONTACT_NAME},

                {COMPANY_NAME} firmasının {INDUSTRY} alanındaki faaliyetlerini inceledik.

                Egebis olarak kurumsal yazılım, SAP danışmanlığı ve
                süreç otomasyonu alanlarında çözümler sunuyoruz.

                Karşılıklı olarak değerlendirebileceğimiz alanlar olup olmadığını
                kısa bir görüşmede konuşmak isteriz.

                Saygılarımızla,
                Egebis Bilişim
                """
        },
        new()
        {
            Id = 100,
            Key = "TAKIP",
            Name = "Takip Maili",
            Subject = "Önceki mesajımız hakkında — Egebis Bilişim",
            Body = """
                Sayın {CONTACT_NAME},

                Kısa bir süre önce {COMPANY_NAME} için gönderdiğimiz mesajı
                hatırlatmak istedik.

                Konu size uygun değilse bilgi vermeniz yeterli, takibi burada
                bırakalım. İlgilenmeniz halinde 20 dakikalık kısa bir görüşme
                için uygun bir zaman önerebiliriz.

                Saygılarımızla,
                Egebis Bilişim
                """
        }
    };
}
