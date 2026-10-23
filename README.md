# VMM Facility Management — Expense Tracker (MVP)

A local web app for the Facility Manager to record expenses and export a PDF report.
It runs only on this Mac (localhost) and works without internet access.

- **Frontend:** React + TypeScript (Vite) in `frontend/`
- **Backend:** ASP.NET Core (.NET 10) + EF Core + SQLite in `backend/VmmFacility.Api/`

## Start the app

Requirements: .NET 10 SDK and Node.js (npm). Both are only needed on this Mac.

```bash
./start.sh
```

The script builds the frontend, starts the backend, and opens <http://localhost:5180> in your
browser. The backend serves the frontend, so this one process runs the whole app.
Press **Ctrl+C** in the terminal to stop it.

The first time you run it, `npm install` downloads the frontend packages, and the backend build
downloads its NuGet packages. After that, the app runs without internet access.

On first launch the app asks you to create the Facility Manager username and password.
Sessions end after 30 minutes of inactivity or when the browser is closed.

## Where data is stored

The SQLite database is at:

```
~/Library/Application Support/VMM Facility Management/vmm-facility.db
```

The same folder holds `keys/`, which signs the sign-in session cookie. Back up the whole
folder to keep your data. Set `VMM_DATA_DIR` to use a different folder.

## Network access

The backend listens only on `127.0.0.1` and `[::1]` (port 5180, or `VMM_PORT`) and only accepts
`localhost` host names. Other devices on the network can't reach it.

## API

All expense endpoints require a signed-in session. Requests that change data must include the
header `X-VMM-Request: 1`. Amounts are sent and returned as decimal strings, such as `"1250.50"`;
dates use `yyyy-MM-dd`.

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/expenses` | List all expenses, newest date first |
| GET | `/api/expenses/{id}` | Read one expense |
| POST | `/api/expenses` | Create an expense |
| PUT | `/api/expenses/{id}` | Edit an expense |
| GET | `/api/expenses/report.pdf` | Download the PDF report |
| GET | `/api/auth/session` | Show whether setup is needed and who is signed in |
| POST | `/api/auth/setup` | Create the single account (first launch only) |
| POST | `/api/auth/login` · `/api/auth/logout` | Sign in or sign out |
| POST | `/api/chat` | Stream an assistant reply from local Ollama (see below) |

## Assistant chat (optional, uses Ollama)

The Expenses page has an **Assistant** button in the bottom-right corner that opens a chat
with a local model. It uses the same sign-in as the rest of the app. The expense tracker works
without Ollama; only the chat needs it.

```bash
ollama pull qwen3.5:9b               # once: chat model
ollama pull qwen3-embedding:0.6b     # once: policy document search
ollama serve                         # or open the Ollama app; listens on http://localhost:11434
./start.sh
```

- The backend calls Ollama at `http://localhost:11434` with model `qwen3.5:9b`. It never uses
  another model or a cloud service. If Ollama isn't running or the model is missing, the chat
  shows an error.
- The browser keeps the conversation in memory only and sends it with each question. Nothing is
  stored. **New chat**, leaving the Expenses page, or refreshing clears it.
- The chat can answer questions about saved expenses, such as "What did we spend on Security last
  month?". The model can call one read-only tool, `get_expense_data`, with optional filters:
  `dateFrom`/`dateTo` (`yyyy-MM-dd`), `category`, `paymentState` and `descriptionContains`. The
  backend validates the filters, queries SQLite through EF Core without saving anything, and computes
  exact INR totals (overall, Paid, Unpaid, by category). It returns them with up to 50 matching
  expenses. The model never runs SQL and can't change records. Tool calls are logged with their
  filters.
- The chat can also answer from policy documents (`search_policy_documents`). It reads every `.md`
  file in `docs/policies/` (or `VMM_POLICY_DIR`), splits each one at its `##`/`###` headings, and
  embeds each section with `qwen3-embedding:0.6b`. The vectors are kept in memory. The index is built
  on the first search and rebuilt when a file changes. A search returns up to 4 sections that score at
  least 0.45, and answers cite the document and section. This needs `ollama pull qwen3-embedding:0.6b`.
- `POST /api/chat` takes `{"messages":[{"role":"user"|"assistant","content":"…"}]}` (the last
  message must be from the user) and returns `application/x-ndjson`, one event per line:
  `{"type":"delta","text":"…"}` for each piece of the reply, then `{"type":"done"}` or
  `{"type":"error","message":"…"}`. Errors before the reply starts are normal problem responses.

## Assistant design notes

These notes record why the assistant is built the way it is, what testing showed, and what is
still open.

### Architecture

```
Browser (ChatPanel.tsx)                ASP.NET Core (Chat.cs)                     Ollama (localhost:11434)
───────────────────────                ──────────────────────                     ────────────────────────
history in memory ── POST /api/chat ─► validate, add system prompt ── /api/chat ─► qwen3.5:9b
                                       │  tool-call loop (≤ 4 rounds) ◄────────── tool_calls / text
                                       │   ├─ get_expense_data ──► EF Core ──► SQLite (read-only)
                                       │   └─ search_policy_documents ── /api/embed ─► qwen3-embedding:0.6b
◄── NDJSON delta/done/error ───────────┘  streams the text of the final answer
```

| File | Role |
|---|---|
| `frontend/src/components/ChatPanel.tsx` | Popup UI, in-memory history, New chat, aborts the request on New chat or leaving the page |
| `frontend/src/api.ts` (`streamChat`) | Sends the history, reads the NDJSON stream, turns stream errors into readable messages |
| `backend/VmmFacility.Api/Chat.cs` | `/api/chat` endpoint, system prompt, tool-call loop, streaming, error mapping |
| `backend/VmmFacility.Api/ExpenseLookup.cs` | `get_expense_data`: validated filters, fixed EF Core query, exact decimal totals |
| `backend/VmmFacility.Api/PolicySearch.cs` | `search_policy_documents`: chunking, embeddings, in-memory index, similarity search |
| `docs/policies/*.md` | Sample policy documents (fictional) that the search reads |

### Key decisions

- **The browser talks only to the backend.** Ollama and SQLite are never exposed to the browser.
  The chat endpoint reuses the app's sign-in cookie and `X-VMM-Request` header check; it has no
  separate login.
- **Streaming format: NDJSON** (one JSON event per line). It's simpler than Server-Sent Events,
  works with `fetch` and POST bodies, and allows partial words. The backend calls Ollama with
  `HttpCompletionOption.ResponseHeadersRead` and flushes after every event, so it never buffers
  the whole answer. `RequestAborted` is passed to every Ollama call, so a browser disconnect stops
  generation.
- **`think: false`.** qwen3.5 can reason before answering. That reasoning delays the first visible
  text, so it is turned off.
- **History lives only in the browser.** Nothing is persisted. Tool results are *not* part of the
  history the browser sends back, so the system prompt requires a fresh tool call for follow-up
  questions (see "What testing showed").
- **Plain `HttpClient`, no Ollama SDK.** Two endpoints with small JSON payloads don't justify a
  dependency. The named client has no overall timeout (replies stream for as long as needed) and a
  5-second connect timeout (a stopped Ollama fails quickly).
- **Tools, not SQL.** The model only fills in validated filter values. The query is fixed in code,
  read-only (`AsNoTracking`, never `SaveChanges`), and unknown fields or bad values are rejected
  with an error the model can explain.
- **Totals are computed in C#, not by the model and not with SQL `SUM`.** Amounts are stored as
  TEXT in SQLite to stay exact. SQL `SUM` would convert them to floating point, so the backend sums
  `decimal`s, as the PDF report does. The model is told to quote totals exactly as returned.
- **RAG is a tool, too.** Qwen writes the search query, can split a question into several searches,
  and can combine policy results with expense results in one answer.
- **Chunking follows the document's structure.** Each `##`/`###` section is one chunk, prefixed with
  the document title, ID and heading path before embedding. Sections over 1,500 characters are
  split at blank lines.
- **The embedding model is separate from the chat model.** `qwen3-embedding:0.6b` turns text into
  1024-number vectors. Vectors are normalised, so a dot product equals cosine similarity. Queries
  (not documents) get an instruction prefix, which this model was trained to expect.
- **Top 4 results, minimum score 0.45.** Measured scores: relevant sections 0.58–0.77; an
  off-topic question's best match 0.40–0.42. Below the minimum, the tool reports "nothing relevant"
  and the model says the documents don't cover the question.
- **In-memory index, no vector database yet.** About 30 chunks take roughly 2 seconds to embed,
  and searching them by brute force takes microseconds. A SHA-256 fingerprint of the files and the
  model name triggers a rebuild when a document changes. A SQLite table was not used because
  `EnsureCreated` doesn't add tables to an existing database.

### What testing showed

These checks were run by hand against a scratch database. They are not automated tests.

| Area | Result |
|---|---|
| Streaming, history, sign-in, cancellation | Worked: pieces arrived 20–50 ms apart; disconnects stopped Ollama cleanly |
| Expense totals (known test data) | Correct after prompt fixes, e.g. Security in September ₹15,700.75 = ₹12,500.50 + ₹3,200.25 |
| Expense follow-ups ("which of those…") | Initially **made-up figures**; fixed with a rule to call the tool again for follow-ups (4/4 afterwards) |
| Paid/unpaid split | Initially filtered to Paid only and claimed Unpaid ₹0.00; fixed by stating that results already include both totals |
| Policy synonyms ("lift cables" → "wire ropes", §5.2) | Correct every time |
| Question not in the documents (visitor parking) | Correctly answered "not covered" every time |
| Question spanning both documents (flooded lift pit) | **Unreliable**: right about half the time. When Qwen's query doesn't mention water, §5.2 drops out of the top 4 |
| Expenses plus contract ("AMC paid so far") | **Unreliable**: "so far" sometimes read as "this quarter" |

Lessons:
- Answer quality depends on the queries the 9B model writes as much as on the search itself.
- Prompt rules help but don't guarantee behaviour.
- Exact figures must always come from code.

### Known limitations

- Not yet verified by hand in a browser: the chat popup UI, the "Ollama not running" error, and
  the "model missing" error.
- The model's default sampling `temperature` is used, so answers vary between runs.
- Long sections with several facts (like AMC §5.2) dilute their vector. Documents without headings
  become one "Overview" section, and text without blank lines isn't split at all.
- There is no automated evaluation set yet, so retrieval quality is judged from a handful of runs.

### Before production use

- **Retrieval quality:**
  - An evaluation set of 30–50 questions with their correct sections, measuring recall@k after each change.
  - Hybrid search (keyword BM25 plus vectors).
  - A reranker.
  - Smaller chunks or one chunk per list item.
- **Documents:**
  - Upload screen.
  - PDF and Word conversion to Markdown, with OCR for scanned files.
  - Version and effective-date tracking, so an expired contract isn't quoted.
  - Access control per document.
- **Answer safety:**
  - Show sources in the UI.
  - Check that cited sections exist.
  - Treat document text as untrusted data (prompt injection).
  - A low temperature for factual answers.
- **Infrastructure:**
  - A larger chat model if the hardware allows.
  - Ollama run as a service with health checks and `keep_alive`.
  - `OLLAMA_NUM_PARALLEL` for several users at once.
  - EF Core migrations instead of `EnsureCreated`.
  - Monitoring that logs tool calls and scores but not private content.
- **Tests:** automated tests for chunking, argument validation and the evaluation set.

### Next step: vector database

Options under consideration, none chosen yet:

| Option | What it adds | Trade-off |
|---|---|---|
| **sqlite-vec** (leading option) | Vectors in a local SQLite file; survives restarts; incremental re-embedding; SQL metadata filters | Exact (brute-force) search, which is fine at this scale; native extension still to be checked on macOS with .NET |
| **Qdrant** (Docker) | HNSW approximate-nearest-neighbour index, payload filters, dashboard; closest to large production systems | An extra service to run next to the app and Ollama |
| Hand-built SQLite table | No dependencies; every part visible | No real index |

## Development

To run with hot reload, use two terminals:

```bash
dotnet run --project backend/VmmFacility.Api   # API on http://localhost:5180
cd frontend && npm run dev                     # UI on http://localhost:5173 (proxies /api)
```

The database schema is created automatically on first start (`EnsureCreated`). If the schema
changes in a later release, add EF Core migrations.

PDF reports are generated with [QuestPDF](https://www.questpdf.com/) under its Community
license. That license is free for organisations with less than USD 1M in annual revenue.
