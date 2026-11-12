using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oy.Api.Data.Migrations
{
    /// <summary>Demo data for the App Store / Google Play review (accounts and contents: demo.md at the repository root).</summary>
    public partial class DemoData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
-- App Store / Google Play review: a closed demo world (see demo.md). Nothing here is real; ON CONFLICT keeps re-runs harmless.
INSERT INTO "Programs" ("Id","Slug","Title","Type","AgeMin","AgeMax","StartDate","EndDate","DaysOfWeek","StartTime","EndTime",
  "Capacity","Price","PricePeriod","PriceOptions","Summary","Description","CoverImageUrl","Location","IsPublished","FeaturedOrder",
  "EnrollmentEnabled","EnrollmentForm","RegistrationForm","CreatedAt","UpdatedAt")
VALUES ('d3a00000-0000-4000-8000-000000000001','demo-uygulama-inceleme','Demo Grup – Uygulama İnceleme','WeeklyClass',5,8,current_date - 14,current_date + 365,
  '{1,2,3,4,5,6,7}','10:00','12:00',12,0,'Monthly','[]',
  'Uygulama mağazası incelemesi için demo grup. Gerçek değildir.',
  'Bu eğitim yalnızca App Store / Google Play incelemesi için oluşturulmuş demo verisidir. Sitede yayınlanmaz.',
  '','Demo Orman Alanı',false,NULL,false,'{}','{}',now(),now())
ON CONFLICT DO NOTHING;

-- One lesson every day (reviewers can try the classroom on any day).
INSERT INTO "Sessions" ("Id","ProgramId","Date","StartTime","EndTime","IsCancelled","IsMakeup","Note","StartedByName","CreatedAt","UpdatedAt")
SELECT gen_random_uuid(),'d3a00000-0000-4000-8000-000000000001',d::date,'10:00','12:00',false,false,'','',now(),now()
FROM generate_series(current_date - 14, current_date + 365, interval '1 day') AS d
WHERE EXISTS (SELECT 1 FROM "Programs" WHERE "Id" = 'd3a00000-0000-4000-8000-000000000001')
  AND NOT EXISTS (SELECT 1 FROM "Sessions" WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001');

INSERT INTO "Staff" ("Id","FullName","Title","Email","Phone","Bio","PhotoUrl","EmploymentType","MonthlyNetSalary","MonthlySgkPremium",
  "MonthlyTaxAndOther","PerLessonFee","IsActive","ShowOnWebsite","CreatedAt","UpdatedAt")
VALUES ('d3a00000-0000-4000-8000-000000000002','Demo Öğretmen','Öğretmen (demo)','','','','','PerLesson',0,0,0,0,true,false,now(),now())
ON CONFLICT DO NOTHING;

INSERT INTO "Assignments" ("Id","ProgramId","StaffId","Role","SalaryAllocationPercent","PerLessonFeeOverride","StartDate","EndDate","CreatedAt","UpdatedAt")
VALUES ('d3a00000-0000-4000-8000-000000000003','d3a00000-0000-4000-8000-000000000001','d3a00000-0000-4000-8000-000000000002','Lead',0,0,current_date - 14,current_date + 365,now(),now())
ON CONFLICT DO NOTHING;

-- Teacher role: classroom only (sees just the demo group's lessons; no enrollments, finance or other programs).
INSERT INTO "Roles" ("Id","Name","Permissions","CreatedAt","UpdatedAt")
VALUES ('d3a00000-0000-4000-8000-000000000004','Öğretmen (demo)','{classroom.use}',now(),now())
ON CONFLICT DO NOTHING;

INSERT INTO "Users" ("Id","Email","FullName","PasswordHash","IsActive","Kind","Phone","RoleId","StaffId","CreatedAt","UpdatedAt")
VALUES
  ('d3a00000-0000-4000-8000-000000000005','demo.ogretmen@okulumyeryuzu.com','Demo Öğretmen','AQAAAAIAAYagAAAAEMnntmkKpAPLm7lEfp4N0bX6wnM6vdm/ELygNgf/8rQ5RPxP2yL5szKF17X2uLtmDg==',true,'Staff','','d3a00000-0000-4000-8000-000000000004','d3a00000-0000-4000-8000-000000000002',now(),now()),
  ('d3a00000-0000-4000-8000-000000000006','demo.veli@okulumyeryuzu.com','Demo Veli','AQAAAAIAAYagAAAAEIiStcjbwkJkKTryG3P70UhlExSCxFb8kCfyvEieckvrtUfTTZ02OpU+ZrkMJCN8Yg==',true,'Guardian','',NULL,NULL,now(),now())
ON CONFLICT DO NOTHING;

INSERT INTO "Enrollments" ("Id","ProgramId","ChildName","ChildBirthDate","ParentName","ParentPhone","ParentEmail","Answers","Note",
  "Status","Fee","FeePeriod","StartDate","EndDate","CreatedAt","UpdatedAt")
VALUES
  ('d3a00000-0000-4000-8000-000000000011','d3a00000-0000-4000-8000-000000000001','Deniz Demo',make_date(extract(year from current_date)::int - 6, 4, 12),'Demo Veli','','demo.veli@okulumyeryuzu.com','{}','',
    'Active',0,'Monthly',current_date - 14,NULL,now(),now()),
  ('d3a00000-0000-4000-8000-000000000012','d3a00000-0000-4000-8000-000000000001','Ege Demo',make_date(extract(year from current_date)::int - 7, 9, 3),'Demo Aile','','','{}','',
    'Active',0,'Monthly',current_date - 14,NULL,now(),now()),
  ('d3a00000-0000-4000-8000-000000000013','d3a00000-0000-4000-8000-000000000001','Lina Demo',make_date(extract(year from current_date)::int - 5, 1, 25),'Demo Aile 2','','','{}','',
    'Active',0,'Monthly',current_date - 14,NULL,now(),now())
ON CONFLICT DO NOTHING;

INSERT INTO "GuardianLinks" ("Id","UserId","EnrollmentId","Relation","CreatedAt","UpdatedAt")
VALUES ('d3a00000-0000-4000-8000-000000000021','d3a00000-0000-4000-8000-000000000006','d3a00000-0000-4000-8000-000000000011','Mother',now(),now())
ON CONFLICT DO NOTHING;

-- Past lessons: attendance and teacher notes, so the guardian and the classroom have something to show (times: Turkey = UTC+3).
INSERT INTO "Attendance" ("Id","SessionId","EnrollmentId","Status","ArrivedAt","MarkedByName","MarkedByUserId","CreatedAt","UpdatedAt")
SELECT gen_random_uuid(), s."Id", e."Id",
  CASE WHEN e."Id" = 'd3a00000-0000-4000-8000-000000000013' AND s."Date" = current_date - 2 THEN 'Absent' ELSE 'Present' END,
  CASE WHEN e."Id" = 'd3a00000-0000-4000-8000-000000000013' AND s."Date" = current_date - 2 THEN NULL ELSE (s."Date" + time '07:05') AT TIME ZONE 'UTC' END,
  'Demo Öğretmen','d3a00000-0000-4000-8000-000000000005',now(),now()
FROM "Sessions" s CROSS JOIN "Enrollments" e
WHERE s."ProgramId" = 'd3a00000-0000-4000-8000-000000000001' AND e."ProgramId" = 'd3a00000-0000-4000-8000-000000000001' AND s."Date" BETWEEN current_date - 3 AND current_date - 1
ON CONFLICT DO NOTHING;
UPDATE "Sessions" SET "StartedAt" = (("Date" + time '07:00') AT TIME ZONE 'UTC'), "EndedAt" = (("Date" + time '09:00') AT TIME ZONE 'UTC'),
  "StartedByName" = 'Demo Öğretmen'
WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001' AND "Date" BETWEEN current_date - 3 AND current_date - 1 AND "StartedAt" IS NULL;

INSERT INTO "ObservationNotes" ("Id","SessionId","ProgramId","EnrollmentId","Text","Tags","AuthorName","AuthorUserId","CreatedAt","UpdatedAt")
SELECT gen_random_uuid(), s."Id", 'd3a00000-0000-4000-8000-000000000001', n.e, n.t, n.tags::jsonb, 'Demo Öğretmen', 'd3a00000-0000-4000-8000-000000000005', now(), now()
FROM "Sessions" s
JOIN (VALUES
  ('d3a00000-0000-4000-8000-000000000011'::uuid, 1, 'Deniz bugün arkadaşlarıyla birlikte dallardan bir köprü kurdu, ekibini sabırla yönlendirdi.', '["Sosyal","Doğa ile bağ"]'),
  ('d3a00000-0000-4000-8000-000000000012'::uuid, 1, 'Ege tırmanma parkurunu ilk kez yardımsız tamamladı.', '["Motor"]'),
  ('d3a00000-0000-4000-8000-000000000011'::uuid, 2, 'Deniz topladığı yaprakları renklerine göre sıraladı ve her birine bir isim verdi.', '["Bilişsel","Yaratıcılık"]')
) AS n(e, ago, t, tags) ON s."Date" = current_date - n.ago
WHERE s."ProgramId" = 'd3a00000-0000-4000-8000-000000000001'
  AND NOT EXISTS (SELECT 1 FROM "ObservationNotes" o WHERE o."ProgramId" = 'd3a00000-0000-4000-8000-000000000001');

INSERT INTO "FeedPosts" ("Id","Text","ImageIds","ProgramId","AuthorUserId","AuthorName","PublishedAt","CreatedAt","UpdatedAt")
VALUES
  ('d3a00000-0000-4000-8000-000000000031','Merhaba aileler! Bu hafta orman yürüyüşümüzde kuş seslerini dinleyip kuş gözlem defterlerimizi başlatacağız. Rahat ayakkabı ve yağmurluk getirmeyi unutmayın.',
    '[]','d3a00000-0000-4000-8000-000000000001',NULL,'Okulum Yeryüzü',now() - interval '1 day',now(),now()),
  ('d3a00000-0000-4000-8000-000000000032','Cuma günü ateş etrafında hikâye saati var. Çocuklarınızın en sevdiği masalı bizimle paylaşmasını bekliyoruz.',
    '[]','d3a00000-0000-4000-8000-000000000001',NULL,'Okulum Yeryüzü',now() - interval '3 days',now(),now()),
  ('d3a00000-0000-4000-8000-000000000033','Yeni dönem takvimimiz hazır! Dersler her gün 10:00–12:00 arasında, Demo Orman Alanı''nda.',
    '[]','d3a00000-0000-4000-8000-000000000001',NULL,'Okulum Yeryüzü',now() - interval '8 days',now(),now())
ON CONFLICT DO NOTHING;

INSERT INTO "Todos" ("Id","Title","Notes","Visibility","Priority","Repeat","RepeatDay","DueDate","CreatedById","CreatedAt","UpdatedAt")
VALUES
  ('d3a00000-0000-4000-8000-000000000041','Orman yürüyüşü için ilk yardım çantasını kontrol et','','Private','High','None',0,current_date,'d3a00000-0000-4000-8000-000000000005',now(),now()),
  ('d3a00000-0000-4000-8000-000000000042','Kuş gözlem defterlerini çoğalt','20 adet','Private','Normal','None',0,current_date + 2,'d3a00000-0000-4000-8000-000000000005',now(),now()),
  ('d3a00000-0000-4000-8000-000000000043','Velilere hafta sonu etkinliğini hatırlat','','Private','Normal','None',0,NULL,'d3a00000-0000-4000-8000-000000000005',now(),now())
ON CONFLICT DO NOTHING;

INSERT INTO "Notifications" ("Id","UserId","Kind","Title","Body","Url","Key","ReadAt","CreatedAt","UpdatedAt")
VALUES
  ('d3a00000-0000-4000-8000-000000000051','d3a00000-0000-4000-8000-000000000006',0,'Okulum Yeryüzü''nden yeni paylaşım','Merhaba aileler! Bu hafta orman yürüyüşümüzde kuş seslerini dinleyip…','/veli','demo:1',NULL,now() - interval '1 day',now()),
  ('d3a00000-0000-4000-8000-000000000052','d3a00000-0000-4000-8000-000000000005',7,'Bugün son gün','Orman yürüyüşü için ilk yardım çantasını kontrol et','/yapilacaklar?is=d3a00000-0000-4000-8000-000000000041','demo:2',NULL,now() - interval '2 hours',now())
ON CONFLICT DO NOTHING;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Children first (attendance and notes do not cascade), then the demo program, people and accounts.
            migrationBuilder.Sql("""
DELETE FROM "Attendance" WHERE "EnrollmentId" IN ('d3a00000-0000-4000-8000-000000000011','d3a00000-0000-4000-8000-000000000012','d3a00000-0000-4000-8000-000000000013') OR "SessionId" IN (SELECT "Id" FROM "Sessions" WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001');
DELETE FROM "ObservationNotes" WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001' OR "EnrollmentId" IN ('d3a00000-0000-4000-8000-000000000011','d3a00000-0000-4000-8000-000000000012','d3a00000-0000-4000-8000-000000000013');
DELETE FROM "ClassPhotos" WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001';
DELETE FROM "GuardianLinks" WHERE "EnrollmentId" IN ('d3a00000-0000-4000-8000-000000000011','d3a00000-0000-4000-8000-000000000012','d3a00000-0000-4000-8000-000000000013');
DELETE FROM "Invoices" WHERE "PaymentId" IN (SELECT "Id" FROM "Payments" WHERE "EnrollmentId" IN ('d3a00000-0000-4000-8000-000000000011','d3a00000-0000-4000-8000-000000000012','d3a00000-0000-4000-8000-000000000013'));
DELETE FROM "Payments" WHERE "EnrollmentId" IN ('d3a00000-0000-4000-8000-000000000011','d3a00000-0000-4000-8000-000000000012','d3a00000-0000-4000-8000-000000000013');
DELETE FROM "Enrollments" WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001';
DELETE FROM "Assignments" WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001';
DELETE FROM "Sessions" WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001';
DELETE FROM "FeedPosts" WHERE "ProgramId" = 'd3a00000-0000-4000-8000-000000000001';
DELETE FROM "Programs" WHERE "Id" = 'd3a00000-0000-4000-8000-000000000001';
DELETE FROM "Todos" WHERE "CreatedById" = 'd3a00000-0000-4000-8000-000000000005';
DELETE FROM "Users" WHERE "Id" IN ('d3a00000-0000-4000-8000-000000000005','d3a00000-0000-4000-8000-000000000006');
DELETE FROM "Roles" WHERE "Id" = 'd3a00000-0000-4000-8000-000000000004';
DELETE FROM "Staff" WHERE "Id" = 'd3a00000-0000-4000-8000-000000000002';
""");
        }
    }
}
