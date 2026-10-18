# لوحة AI Discuss — `ai-disscuss.theroyalgolfclub.com`

لوحة حيّة يظهر عليها نقاش الأعضاء والحكّام في جلسات `/ai-discuss`، بشخصيات بكسل تتكلم وتُكتب رسائلها مباشرة.
مشروع Vercel **مستقل** عن لوحة outlet (لا يلمس بياناتها ولا مفاتيح Odoo).

```
Claude Code (/ai-discuss) ──POST /api/ingest  (Bearer AI_DISCUSS_TOKEN)──▶  Vercel function ──▶  Upstash Redis
                                                                                                      │
المتصفح (خلف تسجيل الدخول) ◀──GET /api/board (استطلاع كل 1.5 ث أثناء جلسة، 15 ث وهي خاملة)────────────────┘
```

## الإعداد لمرة واحدة

هذه الخطوات تحتاج حسابك على Vercel وإدارة DNS، ولا يمكن لـ Claude تنفيذها.

1. **مشروع Vercel ثانٍ من نفس المستودع.** Add New → Project → استورد `Ahmedsami997/RGC`، ثم اضبط
   **Root Directory = `ai-discuss-board`** و Framework Preset = **Other**. سمّه `ai-discuss-board`.
   الكود موجود الآن على الفرع `claude/ai-discuss`: ادمجه في `main`، أو غيّر *Settings → Git → Production Branch*
   إلى `claude/ai-discuss` إلى أن تدمجه.

2. **قاعدة Redis.** في المشروع: *Storage → Create/Connect → Upstash (Redis)* من الـ Marketplace، الخطة المجانية
   تكفي، واربطها بالمشروع. يضيف Vercel المتغيرات تلقائيًا (`KV_REST_API_URL` و`KV_REST_API_TOKEN`، أو
   `UPSTASH_REDIS_REST_URL` و`UPSTASH_REDIS_REST_TOKEN`). الكود يقبل الاسمين.

3. **المتغيرات** (*Settings → Environment Variables*، للإنتاج):

   | الاسم | القيمة |
   |---|---|
   | `DASHBOARD_USERS` | `اسم1:كلمة1,اسم2:كلمة2` (نفس صيغة لوحة outlet) |
   | `AI_DISCUSS_TOKEN` | سرّ عشوائي طوله 32 حرفًا أو أكثر. ولّده على جهازك: `openssl rand -hex 32` |

   لا تكتب السرّ في المحادثة ولا في المستودع. الدالة ترفض كل طلب إذا كان الرمز غير مضبوط أو أقصر من 24 حرفًا.
   أعد النشر بعد إضافة المتغيرات (Redeploy).

4. **النطاق.** *Settings → Domains → Add* → `ai-disscuss.theroyalgolfclub.com` (بحرفي `s` مزدوجين كما طلبت).
   سيعرض Vercel سجل DNS المطلوب (غالبًا CNAME إلى `cname.vercel-dns.com`)، أضفه عند من يدير DNS العام
   لـ `theroyalgolfclub.com`، كما فعلت مع `outlet`.

5. **بيئة Claude Code السحابية** (قائمة البيئة في شريط عنوان الجلسة ← Edit):
   - أضف `AI_DISCUSS_TOKEN` بنفس القيمة، تحت *Network secrets* إن ظهر، وإلا كمتغير بيئة.
   - في *Network access* أضف `ai-disscuss.theroyalgolfclub.com` إلى *Allowed domains* (مستوى Limited) مع
     ترك *Allow package managers* مفعّلًا.

   تلتقط الجلسات الجديدة التغيير. الجلسة الحالية لا.

6. **تحقق.**
   - افتح `https://ai-disscuss.theroyalgolfclub.com`: يظهر طلب اسم المستخدم وكلمة المرور، ثم «بانتظار جلسة».
   - في Claude Code: `python3 .claude/skills/ai-discuss/board_post.py --ping` يجب أن يطبع `ok pong`.
   - شغّل `/ai-discuss <فكرة>` وراقب اللوحة. إن لم يعمل الموقع يكمل الأمر على لوحة claude.ai أو بالنص.

## الأمان

- **القراءة** خلف Basic Auth لكل مسار (`middleware.js`)، ويفشل الدخول مغلقًا إن لم يُضبط أي مستخدم.
- **الكتابة** (`POST /api/ingest`) مستثناة من نافذة الدخول لأن Claude Code لا يجيبها، ولها رمزها الخاص
  (Bearer، مقارنة بزمن ثابت). `GET` وأي مسار آخر يبقى خلف الدخول.
- **المدخلات** تُفحص قبل التخزين: شكل المعرّف، أنواع الأحداث المسموحة، حجم الحدث (20KB)، ودفعة (50 حدثًا)،
  وسقف 400 حدث للجلسة، والألوان لا تقبل إلا `#rrggbb`. تنتهي الجلسات بعد 90 يومًا.
- **العرض**: كل النصوص تُكتب بـ `textContent` فلا تُفسَّر وسوم HTML، وسياسة CSP صارمة في `vercel.json`
  (لا اتصال إلا بالموقع نفسه، ولا تضمين في إطارات).
- كلمات المرور في `DASHBOARD_USERS` نص عادي في متغير بيئة، كما في لوحة outlet.

## التكلفة

الصفحة مصمّمة لتكون رخيصة: استطلاع الصفحة الخاملة أمر Redis واحد كل 15 ثانية (نحو 5,760 يوميًا لكل تبويب مفتوح)،
وكل 1.5 ثانية فقط أثناء جلسة جارية. التبويب المخفي لا يستطلع. راجع حدود خطة Upstash المجانية في لوحتهم.

## الملفات

| الملف | وظيفته |
|---|---|
| `public/index.html` | الصفحة. **مولَّدة**: لا تعدّلها يدويًا (انظر أدناه) |
| `api/ingest.js` | استقبال الأحداث من Claude Code (بالرمز) |
| `api/board.js` | قراءة الجلسات والأحداث للصفحة |
| `api/_redis.js` | عميل Upstash REST صغير بلا مكتبات |
| `middleware.js` | تسجيل الدخول لكل شيء ما عدا `/api/ingest` |
| `vercel.json` | رؤوس الأمان ومنع التخزين المؤقت للـ API |
| `sync.py` | يولّد `public/index.html` من الصفحة المشتركة |
| `test/api.test.mjs` | اختبارات الخادم والحماية |

## تعديل الصفحة

الصفحة مكتوبة مرة واحدة في `.claude/skills/ai-discuss/board.html` (وهي نفسها المنشورة كـ Artifact على claude.ai).
بعد أي تعديل عليها:

```bash
python3 ai-discuss-board/sync.py          # يعيد توليد public/index.html
python3 ai-discuss-board/sync.py --check  # يفشل إن كان المولَّد قديمًا
```

## الاختبار

```bash
cd ai-discuss-board && node --test test/api.test.mjs   # لا يحتاج تثبيت شيء
```

## واجهة الاستقبال

```
POST /api/ingest        Authorization: Bearer <AI_DISCUSS_TOKEN>
{ "sid": "s-20261018-101500",
  "session": { "idea": "...", "status": "running", "cast": {"members": ["analyst", ...]}, "castMeta": {...} },
  "events":  [ { "kind": "speech", "who": "analyst", "stance": "SUPPORT", "text": "...", "extras": [...] } ] }
{ "ping": true }        → { "ok": true, "pong": true }
```

أنواع الأحداث: `phase` · `thinking` · `speech` · `score` · `note` · `decision` · `final` · `end`.
الخادم يرقّم الأحداث (`seq`) بنفسه. الحقول التفصيلية موصوفة في `.claude/skills/ai-discuss/SKILL.md`.
