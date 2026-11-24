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
- لا يحتاج تنصيبا: ملف واحد يعمل على ويندوز 10/11.

## التنزيل

من صفحة [الإصدارات (Releases)](../../releases/latest) نزّل الملف `EngStores-<الإصدار>.zip` وفك ضغطه في مجلد،
ثم شغّل «Inventory Management.exe». الملف `‎.sha256` بجانبه هو بصمة التحقق من سلامة التنزيل.

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

1. **على الحاسوب الذي سيكون الخادم:** في شاشة الدخول افتح «إعدادات الاتصال» واضغط «السيرفر الرئيسي»،
   وأدخل كلمة سر الإعداد التي يعطيها مسؤول النظام، ثم وافق على نافذة صلاحيات ويندوز.
2. **انتظر** حتى ينتهي التنصيب ونقل البيانات (10–20 دقيقة، مع شرح التقدم). احتفظ باسم الحاسوب الذي يظهر في النهاية،
   وأوقف وضع السكون في هذا الحاسوب.
3. **على كل حاسوب آخر:** «إعدادات الاتصال» ← «سيرفر ثانوي»؛ يجد البرنامج الخادم في الشبكة ويتصل به تلقائيا.

التفاصيل في [إعداد الخادم المجاني.txt](إعداد%20الخادم%20المجاني.txt).

## البناء من الكود

لا يحتاج أي برنامج إضافي: مترجم C# الموجود في ويندوز (.NET Framework 4).

```
build.bat                 ← Inventory Management.exe
tests\build-tests.bat     ← برامج الاختبار في tests\bin
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

- **Download:** [Releases](../../releases/latest) → `EngStores-<version>.zip`, extract, run the .exe (no installation).
- **User manual (Arabic, illustrated):** [docs/manual/index.html](docs/manual/index.html), shipped in the zip and opened
  from the program with F1. Its screenshots come from fake demo data via `tests\bin\ManualShots.exe`.
- **Build:** `build.bat` (uses the C# compiler that ships with Windows, no SDK/NuGet). Tests: `tests\build-tests.bat`.
- **Tests:** `tests\RunAll.ps1` seeds fake demo data into `tests\demo\empty-template.mdb` (the real schema, no rows)
  and runs the core and year-closing tests; `-Data <file>` runs them on a copy of real data. CI runs on every push / PR.
- **Release:** bump `Program.Version` in `src/App/Program.cs`, push a tag `vX.Y.Z`; `.github/workflows/release.yml`
  builds, tests, zips and publishes the release.
- **Contributing:** see [AGENTS.md](AGENTS.md) (C# 5, x86, one class per file, rules for SQL and the shared tables).
