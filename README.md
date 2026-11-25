<div dir="rtl">

# نظام إدارة المخازن • Inventory Management Software

برنامج ويندوز لإدارة المخازن (مواد، أرصدة، سندات، مراكز كلفة) بواجهة عربية بالكامل.
يعمل على ملف بيانات Access (‎.AMR / .mdb‎) الخاص بالبرنامج القديم دون أن يتعطل البرنامج القديم،
أو على خادم SQL Server Express المجاني داخل الشركة ليعمل عدة حواسيب على نفس البيانات في نفس الوقت.

[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)

## المميزات

- **السندات:** إدخال مخزني (محلي / استيراد)، إخراج مخزني إلى مركز كلفة، مردود إخراج مخزني، مردود مشتريات، نقل مخزني، حجز مواد لمركز كلفة.
- **أرصدة صحيحة دائما:** كل حفظ أو حذف في عملية واحدة، مع ربط الإخراج بأقدم إدخال (FIFO) كما في البرنامج القديم، وأرقام سندات لا تتكرر.
- **حماية من التعارض:** لا يكتب حاسوبان فوق نفس السند؛ من يحفظ ثانيا يُنبَّه.
- **مراكز الكلفة والحجوزات:** مركز الكلفة (مشروع، قسم، صيانة، إدارة، أخرى) يُختار من قائمة في سند الإخراج ويُضاف من نفس السند بزر «+»؛ كشف كلفة لكل مركز كلفة، والإخراج حسب مراكز الكلفة مع مجموع كل نوع، وكميات محجوزة لا تُصرف لغيره.
- **بحث فوري** عن المادة بالرمز أو بأي جزء من الاسم (عربي أو إنجليزي).
- **تقارير** تُطبع وتُصدَّر إلى Excel: جرد الأرصدة، الأرصدة الحرجة مع الكمية المقترحة، كارت المادة، المواد الراكدة، كشف مركز كلفة، الملخص الشهري، وغيرها.
- **إقفال السنة:** نسخة احتياطية + أرشيف جرد نهاية السنة، ثم منع أي تعديل على سنوات مقفلة (يمكن للمدير إعادة الفتح).
- **وضع المبتدئ والوضع المتقدم** لكل مستخدم، وصلاحيات لكل مستخدم، وسجل بكل العمليات.
- **نسخ احتياطي تلقائي** يومي، و**تحديث تلقائي** من صفحة الإصدارات مع التحقق من بصمة الملف (SHA-256).
- **برنامج تنصيب** يستبدل النسخة القديمة بالجديدة تلقائيا دون المساس بالبيانات والإعدادات، ولا يحتاج صلاحيات المسؤول. ويندوز 10/11.

## التنزيل

من صفحة [الإصدارات (Releases)](../../releases/latest) نزّل **`Inventory-Management-Setup-<الإصدار>.exe`** وشغّله:

- **حاسوب جديد:** اضغط «تنصيب». لا يحتاج صلاحيات المسؤول، ويضع أيقونة على سطح المكتب وفي قائمة ابدأ.
- **توجد نسخة قديمة:** يجدها بنفسه ويعرض «وُجدت نسخة قديمة … وستُستبدل تلقائيا»؛ اضغط «تحديث الآن» فقط.
  يطلب من البرنامج المفتوح (ولو كان بجانب الساعة) أن يُغلق بعد سؤال الحفظ المعتاد، ثم يستبدل الملفات مع نسخة احتياطية منها
  واسترجاع تلقائي عند أي خطأ. البيانات والإعدادات والشعار والنسخ الاحتياطية ومجلد «مكونات» لا تُمسّ.
  (النسخة 1.2.0 وما قبلها لا تُغلق تلقائيا: احفظ عملك وأغلقها من أيقونتها بجانب الساعة ← خروج.)
- **الإزالة:** إعدادات ويندوز ← التطبيقات. تُزال ملفات البرنامج والاختصارات فقط، وتبقى البيانات والإعدادات والنسخ الاحتياطية.

> **تنبيه SmartScreen:** برنامج التنصيب غير موقّع رقميا (شهادة التوقيع ليست مجانية)، لذلك قد يعرض ويندوز عند أول تشغيل
> «Windows protected your PC». اضغط **More info (مزيد من المعلومات)** ثم **Run anyway (التشغيل على أي حال)**، بعد التأكد أنك نزّلته
> من هذه الصفحة. ملف `‎.sha256` بجانبه للتحقق من سلامة التنزيل: `certutil -hashfile <الملف> SHA256`.

بديل بلا تنصيب: الملف `EngStores-<الإصدار>.zip` (يُفك في مجلد ويُشغَّل منه «Inventory Management.exe»)، وهو أيضا ما يستعمله التحديث
من داخل البرنامج. إذا شغّلت نسخة جديدة من مجلد جديد وكانت لديك نسخة أقدم فسيسألك البرنامج مرة واحدة هل تُستبدل القديمة بها.
التنصيب الصامت: `Setup.exe /S [/D=المجلد]` (رموز الخروج في `installer/SetupEngine.cs`).

## أول تشغيل

1. يسأل البرنامج عن البيانات: اختر ملف البيانات (‎.AMR أو ‎.mdb) أو اتصل بخادم.
   عند أول فتح لملف قديم يضيف البرنامج جداوله الخاصة (مراكز الكلفة، الحجوزات، السجل) بعد نسخة احتياطية تلقائية —
   يجب أن يكون البرنامج القديم مغلقا في تلك اللحظة فقط.
2. سجّل الدخول بحساب المدير (المستخدم رقم 1)، وغيّر كلمة المرور فورا من أعلى الشاشة.
3. أضف المستخدمين وصلاحياتهم من «المستخدمون».

التفاصيل في [دليل التشغيل.txt](دليل%20التشغيل.txt).

## دليل الاستخدام المصوّر

[**دليل الاستخدام**](docs/manual/index.html): شرح مفصّل بالصور لكل شاشات البرنامج خطوة بخطوة (التشغيل الأول، السندات،
طلبات الصرف والشراء، الجرد، التقارير، الإعدادات، أسئلة شائعة). يأتي مع البرنامج في مجلد `docs\manual` ويُفتح من داخله
بالمفتاح F1 أو من «الإعدادات ← دليل الاستخدام»، ويمكن طباعته (كل فصل في صفحة جديدة).
الصور مأخوذة من بيانات تجريبية وتُعاد بالأداة `tests\bin\ManualShots.exe` (انظر AGENTS.md).

## إعداد الخادم (عدة حواسيب) في 3 خطوات

**الأسهل:** نزّل `Inventory-Management-Full-Setup-<الإصدار>.exe` وشغّله على الحاسوب الذي سيكون الخادم: ينصّب البرنامج وSQL Server
Express (بعد موافقتك على شروط مايكروسوفت) ويجهّز الخادم وحده بدون إنترنت، ثم يعرض كلمة سر الخادم.

أو يدويا:

1. **على الحاسوب الذي سيكون الخادم:** في شاشة الدخول افتح «إعدادات الاتصال والسيرفر» واضغط «السيرفر الرئيسي»،
   واختر كلمة سر للسيرفر، ثم وافق على نافذة صلاحيات ويندوز. التنصيب **بدون إنترنت** من الملف
   `مكونات\SQLEXPR_x64_ENU.exe` بجانب البرنامج (حزمة SQL Server Express المجانية من مايكروسوفت، 266 ميغابايت، موجودة
   داخل ملف Full-Setup فقط). إذا لم يكن موجودا يسألك البرنامج: اختياره من فلاش أو الشبكة، أو تنزيله من موقع مايكروسوفت.
2. **انتظر** حتى ينتهي التنصيب ونقل البيانات (عادة 4–10 دقائق، مع شرح التقدم). احتفظ باسم الحاسوب الذي يظهر في النهاية،
   وأوقف وضع السكون في هذا الحاسوب.
3. **على كل حاسوب آخر:** «إعدادات الاتصال» ← «سيرفر ثانوي»؛ يجد البرنامج الخادم في الشبكة ويتصل به تلقائيا.

التفاصيل في [إعداد الخادم المجاني.txt](إعداد%20الخادم%20المجاني.txt).

## البناء من الكود

لا يحتاج أي برنامج إضافي: مترجم C# الموجود في ويندوز (.NET Framework 4).

```
build.bat                 ← Inventory Management.exe
installer\build-setup.bat ← dist\Inventory Management Setup.exe (بعد build.bat)
tests\build-tests.bat     ← برامج الاختبار في tests\bin
scripts\make-offline.ps1  ← نسخة كاملة بدون إنترنت (مع حزمة SQL Server) للنسخ إلى فلاش؛ لا تُنشر
```

## الاختبارات

الاختبارات تعمل دائما على **نسخة** من البيانات، ولا تحتاج بيانات الشركة:
`tests\demo\empty-template.mdb` ملف فارغ ببنية ملف البيانات نفسها (بلا أي سجل)، ويملؤه `DemoData.exe` ببيانات تجريبية وهمية.

```
powershell -ExecutionPolicy Bypass -File tests\RunAll.ps1              ← بيانات تجريبية (كما في CI)
powershell -ExecutionPolicy Bypass -File tests\RunAll.ps1 -Data <ملف>  ← على نسخة من بيانات حقيقية (الأصل لا يتغير)
tests\bin\DemoData.exe tests\demo\empty-template.mdb demo.mdb          ← ملف تجريبي لتجربة البرنامج
```

كل دفعة (push) وكل طلب دمج (pull request) يُبنى ويُختبر تلقائيا على GitHub Actions.
الإصدار: غيّر `Program.Version` في `src/App/Program.cs` ثم ادفع وسما مثل `v1.2.0` فيُنشر الإصدار تلقائيا.

للمطورين وأدوات الذكاء الاصطناعي: القواعد وطريقة العمل في [AGENTS.md](AGENTS.md).

</div>

---

## English

**نظام إدارة المخازن** (Inventory Management Software) is a Windows desktop warehouse program with an Arabic (RTL) UI:
items, stock balances per store, receipts / issues to projects / returns / transfers / reservations, FIFO cost links,
project cost statements, printable reports with Excel export, year-end closing, users and permissions, automatic backups
and self-update from GitHub Releases (SHA-256 verified). Data lives in the original Access file (.AMR/.mdb) — the old
program keeps working — or in a free SQL Server Express on the office network for several computers at once.

- **Download:** [Releases](../../releases/latest) → `Inventory-Management-Setup-<version>.exe` (Arabic / English, no administrator
  rights). It finds an older copy and replaces it automatically: the running program is asked to exit the normal way, the
  files are replaced with backup and rollback, data / settings / logo / backups are never touched. Silent: `/S [/D=folder]`.
  The file is not code-signed (no free certificate), so SmartScreen may show "Windows protected your PC": *More info* →
  *Run anyway*; check the `.sha256` next to it. Without installing: `EngStores-<version>.zip`, extract and run the .exe.
- **User manual (Arabic, illustrated):** [docs/manual/index.html](docs/manual/index.html), shipped in the zip and opened
  from the program with F1. Its screenshots come from fake demo data via `tests\bin\ManualShots.exe`.
- **Server setup without internet:** «السيرفر الرئيسي» installs SQL Server Express from `مكونات\SQLEXPR_x64_ENU.exe` (inside the release's
  Full-Setup file, unmodified and under Microsoft's terms in `installer/sql-express-terms.pdf`, which the user accepts; Microsoft and
  SQL Server are trademarks of Microsoft, which does not endorse this program; `scripts\make-offline.ps1` builds the complete offline folder). Downloading it is offered, never done on its own.
- **Build:** `build.bat` (uses the C# compiler that ships with Windows, no SDK/NuGet). Tests: `tests\build-tests.bat`.
- **Tests:** `tests\RunAll.ps1` seeds fake demo data into `tests\demo\empty-template.mdb` (the real schema, no rows)
  and runs the core and year-closing tests; `-Data <file>` runs them on a copy of real data. CI runs on every push / PR.
- **Release:** bump `Program.Version` in `src/App/Program.cs`, push a tag `vX.Y.Z`; `.github/workflows/release.yml`
  builds, tests, zips and publishes the release.
- **Contributing:** see [AGENTS.md](AGENTS.md) (C# 5, x86, one class per file, rules for SQL and the shared tables).
