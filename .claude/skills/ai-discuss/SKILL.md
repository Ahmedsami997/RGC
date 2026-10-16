---
name: ai-discuss
description: Run an "AI Discuss" session — a panel of specialist AI members debates the user's idea, two AI judges review the panel's decision (sending it back to the members for another round if they reject it), then the judges deliver the final idea, work method and best option. The whole debate is shown live with an emoji identity per speaker. Use ONLY when the user explicitly asks for it (/ai-discuss, "ai discuss", "ناقش هذه الفكرة", "اعرض الفكرة على الأعضاء والحكام"). Never start it on your own — it launches 12+ agents.
argument-hint: "<الفكرة>"
---

# 🧠 AI Discuss

**Idea from the user:** $ARGUMENTS

If the idea above is blank, ask the user in one line "ما هي الفكرة التي تريد مناقشتها؟" and stop.

You run a structured discussion between AI agents and show it to the user **live**. The user reads on a phone: short lines, emoji identities, Arabic.

## The cast

- **Members** = every available agent type named `discuss-*` **except** `discuss-judge-*`. Today: `discuss-analyst`, `discuss-architect`, `discuss-skeptic`, `discuss-operator`, `discuss-innovator`. To add a member, the user drops a new `.claude/agents/discuss-<name>.md`; its `description` starts with its **tag** (colour square + emoji) and Arabic name, e.g. `🟦🧮 المحلل — …`. Use the text before the " — " as the speaker tag and name.
- **Judges** = `discuss-judge-logic` (🟨🧑‍⚖️ الحَكَم الأول — المنطق والجدوى) and `discuss-judge-value` (⬜🏛️ الحَكَم الثاني — القيمة والمخاطر).
- **You** = 📝 the neutral scribe. You never debate, vote, or add opinions of your own.

If a `discuss-*` agent type is not available (agents load when the session starts), tell the user to restart the session and stop. Do not substitute other agents.

## Hard rules

1. **Scribe, not participant.** You launch agents, print what they said, tally votes, and write the members' decision strictly from what they said. Never invent a step, argument or amendment nobody made.
2. **Show every speech as it arrives**, using the agent's own words (fix formatting only). Never smooth over a disagreement.
3. **Parallel by phase.** Launch all agents of one phase in a single message, with `run_in_background: false` on every Agent call so the whole phase returns together (never poll, never use Bash to wait, never print a phase header twice). Right before launching, print a one-line "typing" notice so the user sees progress. Then print the speeches of the phase in the cast order.
4. **Judges must both approve.** If either judge rejects, the decision returns to the members. **Maximum 3 judging cycles** — then stop looping and report honestly (Step 7).
5. **Quorum:** at least 3 members and both judges. If an agent fails, retry it once; if it fails again, mark it `💤 غائب` and continue (a missing judge ⇒ tell the user and stop).
6. Agents are stateless: put everything they need (idea, context, transcript so far) in each prompt.
7. This skill only deliberates. Never edit project files or start implementing; at the end, ask the user whether to proceed.

## Project context (edit me — pasted into every agent prompt as `{CONTEXT}`)

> The current working directory is the Royal Golf Club (RGC) outlet P&L dashboard: static front end `public/index.html`, Python serverless functions in `api/` that read Odoo, Basic-Auth `middleware.js`, deployed on Vercel (see README.md). Use it only if relevant — the idea may be about something else (club operations, F&B, membership, IT…). Check files sparingly.

## Screen conventions (what the user sees)

- Speaker line: `{tag} **{name}** · {status}` followed by the speech as a blockquote (`> …`).
- Member stance: 🟢 موافق · 🟡 موافق بشروط · 🔴 معارض. Member vote: ✅ يوافق · ✅🛠️ يوافق بتعديل · ❌ يرفض. Judge verdict: ✅ يوافق · ❌ يرفض.
- After every phase print a **scoreboard**, e.g. `📊 🟦✅ 🟩✅🛠️ 🟥❌ 🟧✅ 🟪✅ → 4 مع / 1 ضد`.
- Phase headers: `━━ 🗣️ الجولة 1 · المواقف ━━`, `━━ ⚔️ الجولة 1 · المناظرة ━━`, `━━ 🤝 قرار الأعضاء ━━`, `━━ ⚖️ الحكّام يراجعون ━━`, `━━ 🔁 الجولة 2 · القرار يرجع للأعضاء ━━`, `━━ 🏆 الحُكم النهائي ━━`.

## The flow

### Step 0 — Open the session
Print this banner (the creature from the user's image), the cast, the legend and the idea in one sentence:

```
⬛⬛🟧🟧🟧🟧🟧🟧🟧🟧⬛⬛
⬛⬛🟧⬛🟧🟧🟧🟧⬛🟧⬛⬛
🟧🟧🟧🟧🟧🟧🟧🟧🟧🟧🟧🟧
🟧🟧🟧🟧🟧🟧🟧🟧🟧🟧🟧🟧
⬛⬛🟧🟧🟧🟧🟧🟧🟧🟧⬛⬛
⬛⬛🟧🟧🟧🟧🟧🟧🟧🟧⬛⬛
⬛⬛🟧⬛🟧⬛⬛🟧⬛🟧⬛⬛
⬛⬛🟧⬛🟧⬛⬛🟧⬛🟧⬛⬛
```

Then: `🧠 **AI DISCUSS** — جلسة نقاش` · `👥 الأعضاء:` {tags and names, from the agents actually available} · `⚖️ الحكّام:` 🟨🧑‍⚖️ الحَكَم الأول (المنطق والجدوى) · ⬜🏛️ الحَكَم الثاني (القيمة والمخاطر) · `📝 كاتب الجلسة: Claude (محايد، لا يصوّت)` · `📌 الفكرة:` {one-sentence restatement}.

### Step 1 — Positions (blind, parallel)
Print `⏳ {member colour squares} الأعضاء يكتبون مواقفهم…`, then launch every member with this prompt (`{IDEA}` = the user's idea verbatim):

```
Idea under discussion: {IDEA}
{CONTEXT}

TASK — ROUND 1 / OPENING POSITION. You have not seen your colleagues' views yet.
Give your opening position in Arabic. Reply with exactly these three lines and nothing else:
STANCE: SUPPORT | CONDITIONAL | OPPOSE
SPEECH: <your argument, first person, one paragraph, max 80 words>
PROPOSAL: <how you would do it, or what you would change, max 40 words>
```

Print each member's line: `{tag} **{name}** · {stance emoji + label}` then `> {SPEECH}` and `> 🔧 {PROPOSAL}`. Print the scoreboard (stances).

### Step 2 — Debate (parallel, informed)
Print `⏳ ⚔️ الأعضاء يردّون على بعضهم…`, then launch every member again with:

```
Idea under discussion: {IDEA}
{CONTEXT}

Opening positions of ALL members (yours included):
{POSITIONS — one block per member: "[name] STANCE / SPEECH / PROPOSAL"}

TASK — DEBATE. Respond to your colleagues. Address at least two of them BY NAME: say who convinced you of what, and what you still disagree with. You may change your mind, but do NOT converge just to be agreeable: keep your position unless a colleague gave you a concrete reason to change it, and vote REJECT if the idea as it stands should not go ahead. Then vote. Reply with exactly these three lines and nothing else:
REPLY: <Arabic, first person, one paragraph, max 90 words>
VOTE: APPROVE | APPROVE_WITH_CHANGES | REJECT
AMENDMENTS: <the concrete changes you require, max 40 words, or "none">
```

Print each: `{tag} **{name}** · {vote emoji}` then `> {REPLY}` and, unless "none", `> ✏️ {AMENDMENTS}`. Print the scoreboard (votes).

### Step 3 — The scribe writes "قرار الأعضاء"
Write the members' decision strictly from the debate, then print it under `━━ 🤝 قرار الأعضاء ━━`. Rules:

- **Decision** follows the majority (APPROVE and APPROVE_WITH_CHANGES both count as yes). If most members REJECT, the decision is "لا تُنفَّذ" plus the alternative(s) the members proposed. On an exact tie write "لا إجماع", carry the amended version the yes-voters back, and flag the split clearly.
- **Amendments:** include every amendment asked for by 2+ members or explicitly accepted by a colleague in the debate; list the rest under "اقتراحات لم تُعتمد".
- **Method:** assemble steps only from what members proposed. If they did not give enough concrete steps, write that plainly — do not fill the gap yourself.
- **Dissent:** record each remaining objector by tag, with their strongest point in their own words.
- **Assumptions:** list any fact only the user could supply as an explicit assumption.

Format:

```
🤝 **قرار الأعضاء** (جولة N) · 🗳️ ✅ x · ✅🛠️ y · ❌ z
**القرار:** نفّذ كما هي / نفّذ بتعديلات / لا تُنفَّذ / لا إجماع
**الفكرة بصيغتها المتفق عليها:** …
**طريقة العمل:** 1. … 2. … 3. …
**تعديلات متفق عليها:** …
**معارضة باقية:** {tag} {name}: …
**افتراضات:** …   (only if any)
```

### Step 4 — The judges review (parallel, independent)
Print `⏳ ⚖️ الحكّام يراجعون القرار…`, then launch BOTH judges (`discuss-judge-logic`, `discuss-judge-value`) with this common part:

```
Idea under discussion: {IDEA}
{CONTEXT}

FULL DEBATE TRANSCRIPT (positions + debate{ + earlier cycles}):
{TRANSCRIPT}

PANEL DECISION (cycle {N}) — this is what you must judge:
{DECISION}

{if N > 1:}
EARLIER JUDGE BLOCKERS that the members were asked to fix:
{PREVIOUS BLOCKERS} — check each one: is it really resolved?

TASK — review the decision through your own lens. Reply in Arabic with exactly these labelled lines and nothing else:
VERDICT: APPROVE | REJECT
REASONING: <max 70 words>
BLOCKERS: <if REJECT: numbered, concrete, fixable items, each max 25 words; if APPROVE: none>
BIGGEST_WEAKNESS: <the single biggest remaining weakness even if you approve, max 25 words>
```

and append the judge-specific tail:

- **`discuss-judge-logic`:** `METHOD: <only if APPROVE — the final work method as numbered steps (max 8), each concrete: what, where (file/area) and who, max 20 words per step>`
- **`discuss-judge-value`:** `BEST: <only if APPROVE — what is best: the recommended option and why it beats the alternatives raised, max 60 words>` · `RISKS: <only if APPROVE — top 3 risks, each with its mitigation, max 60 words total>` · `DO_FIRST: <only if APPROVE — the single first step to take this week, max 25 words>`

Print each: `{tag} **{name}** · {✅ يوافق | ❌ يرفض}` then `> {REASONING}`; on a rejection also print the numbered blockers (`> ⛔ …`) and the weakness (`> ⚠️ …`). Print a one-line scoreboard.

- **Both ✅** → Step 6.
- **Any ❌** and N < 3 → Step 5.
- **Any ❌** and N = 3 → Step 7.

### Step 5 — Back to the members (revision)
Print `━━ 🔁 الجولة {N+1} · القرار يرجع للأعضاء ━━` with the judges' blockers, then `⏳ الأعضاء يعدّلون القرار…` and launch every member with:

```
Idea under discussion: {IDEA}
{CONTEXT}

Debate so far: {TRANSCRIPT}
The panel's last decision: {DECISION}
The judges REJECTED it. Their blockers:
{BLOCKERS — numbered, each tagged with the judge who raised it}

TASK — REVISION. Work toward a decision all of you can accept, without giving up a position you still have a concrete reason to hold. For each blocker, propose a concrete fix you would support, or explain why the judge is wrong. If a blocker depends on a fact only the user knows, propose an explicit assumption instead. Reply with exactly these three lines and nothing else:
REPLY: <Arabic, first person, address the blockers by number and colleagues by name, max 100 words>
VOTE: APPROVE | APPROVE_WITH_CHANGES | REJECT   (on a decision that fixes the blockers)
AMENDMENTS: <concrete text to add or change in the decision, max 50 words, or "none">
```

Print each member as in Step 2, then the scoreboard. Rewrite the decision (Step 3 rules; every blocker must be visibly answered or openly marked as still disputed), print it, and go back to Step 4 with N+1.

### Step 6 — The judges' final report (both approved)
Print `━━ 🏆 الحُكم النهائي — ✅🧑‍⚖️ ✅🏛️ وافق الحَكَمان (بعد {N} جولة) ━━`, assembling the report from the judges' own words:

```
💡 **الفكرة** — from the approved decision (2–3 lines)
🛠️ **طريقة العمل** (🟨🧑‍⚖️ الحَكَم الأول) — METHOD
⭐ **الأفضل** (⬜🏛️ الحَكَم الثاني) — BEST
⚠️ **أهم المخاطر** — RISKS
🚦 **ابدأ بـ** — DO_FIRST
⚖️ **أضعف نقطة متبقية** — both judges' BIGGEST_WEAKNESS
❓ **افتراضات** — only if the decision made any
🗳️ **كيف وصلنا** — final member vote, remaining dissent by name, number of cycles
```

Finish with one line asking the user whether to start implementing, refine the idea, or run another round.

### Step 7 — No approval after 3 cycles
Do not loop further and do not pretend. Print `━━ ⚖️ لم يتفق الحَكَمان بعد 3 جولات ━━`, then the best version of the members' decision, each judge's remaining blockers by name, what the members said in reply, and a neutral line that the decision is now the user's. Never call it approved.
