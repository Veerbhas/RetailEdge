# Assessment Prompts

This file records the user prompts used during this assessment.

## 1. Project structure request

```text
create project structure like below

RetailEdge/
├── .github/
│   └── copilot-instructions.md
├── src/
│   ├── inventory/
│   │   ├── [model file]           # AI-generated, unreviewed
│   │   └── [service file]         # AI-generated, unreviewed
│   └── cart/
│       └── (empty — your feature goes here)
├── tests/                         # Empty
├── README.md                      # Needs your tech stack declaration
└── [dependency file]
```

## 2. Empty structure clarification

```text
create only structure with empty file content , i will give my assignment details later then update files
```

## 3. Case study README review request

```text
i have to work on below case study , review and update README.md file

Case Study: RetailEdge
— Cart Abandonment Recovery System

Background

You are an AI Augmented Software
Engineer at RetailEdge, a mid-sized omnichannel retailer with both an
e-commerce platform and physical stores across the country. RetailEdge operates
a microservices-based backend that powers the customer-facing web store, mobile
app, and in-store POS terminals. The engineering team is actively adopting
GitHub Copilot as its standard AI development tool.

The product team has escalated a
high-priority initiative for the upcoming sprint: a Cart Abandonment Recovery
System. Analytics have revealed that over 60% of online shoppers add items to
their cart but leave without completing a purchase. RetailEdge wants to
automatically detect these abandoned carts and trigger personalised recovery
workflows — discount vouchers, reminder notifications, and restocking alerts —
to convert lost intent into completed orders.

You have been assigned this
feature. Your tech lead has given you an initial brief, a piece of existing
code to work with, and a set of delivery expectations. You will use GitHub
Copilot throughout — but your job is not just to generate code. You are expected
to engineer the context that shapes AI output, validate what Copilot produces,
specify intent with precision, and design systems that hold up under production
scrutiny.

Part 1 — Tech Lead’s
Brief

“We need to tackle cart
abandonment properly this sprint. Here is what the product team is asking for:

A customer browses the store,
adds items to their cart, and then leaves without checking out. After 30
minutes of inactivity, the system should flag the cart as abandoned. Once
flagged, it should trigger a recovery workflow — send the customer a personalised
notification (email or push), optionally attach a one-time discount voucher if
the cart value exceeds a threshold, and log the event for analytics. If the
customer completes the purchase within 24 hours, mark the recovery as
successful and record the conversion.

One more thing — last sprint,
a junior engineer used Copilot to generate our Inventory Sync module. It was
committed directly to the repo and has never been reviewed properly. It is
sitting under src/inventory/. I need you to review it before we build anything
that depends on it. Fix what needs fixing and make sure it meets our
engineering standards.

Use GitHub Copilot for
everything — but do it right. Set up the project context file before you write
a single line of feature code. I want clean architecture, solid tests, proper
security thinking, and a PR I can review with confidence.”
```

## 4. Prompt archive request

```text
Important Instruct : save all
prompts used during this assessment in a separate file (PROMPTS.md) for
reference and review.
```

## 5. Automatic prompt saving question

```text
prompts which will use it will automaticly save into file ?
```

## 6. Prompt recording process request

```text
then how can save used prompts during this assignment in prompts.md
```

## 7. Copilot instructions update request

```text
please update in copilot-instuction.md
```

## 8. README workflow instruction request

```text
i want rule in instruction.md - going forward whenever i asked to do any task , any file update or any assignment step then it should read readme.md file for casestudy info of assignment and work acordingly
```

## 9. Detailed Copilot instructions request

```text
update instructions.md file and it should cover below points

•	Technology stack declaration
•	Architecture conventions (model → repository → service → controller)
•	Coding standards (naming, type annotations, structured logging)
•	Retail-specific domain rules (decimal types for currency — never float, store_id/customer_id scoping, customer PII handling)
•	Security rules: JWT algorithm pinned to RS256 or HS256, DTO whitelisting on all write endpoints, token expiry validation (exp/iat/nbf)
•	Broken Object Level Authorisation rule with scoping to authenticated user context
•	Performance rules: cursor-based pagination required, configurable batch processing for detection scans, minimum 2 database index declarations
•	Testing expectations
```

## 10. Security, performance, and authorisation compliance request

```text
ensure instructions.md have below details if not please update

**Security, Performance & Authorisation Rules (Required
in copilot-instructions.md)**

The following rules must be
explicitly defined in your context file and will be evaluated as part of
Section A scoring:

**Security:**

• JWT algorithm must be explicitly pinned to RS256 or
HS256 — the alg: none attack vector must be rejected at the middleware layer;
Copilot-generated JWT verification code must be reviewed for this vulnerability

• All write endpoints must use a Data Transfer Object
(DTO) that explicitly whitelists permitted fields — direct model binding from
the request body is prohibited; mass assignment is a common AI-generated
vulnerability

• JWT middleware must explicitly validate exp, iat, and
nbf claims in addition to the signature — token expiry validation is frequently
omitted by AI-generated authentication code; failure to validate any claim must
return 401 with a specific error code

**Performance:**

• All list endpoints must implement cursor-based
pagination using cursor and limit request parameters and nextCursor and hasMore
in the response — offset-based pagination using page or skip is explicitly
prohibited

• The cart abandonment detection scan must accept a
configurable batchSize parameter; each batch must be committed independently so
a mid-run failure does not roll back the entire scan; the scan must be
restartable from the last successfully processed cursor

• A minimum of 2 database indexes must be declared in
SPEC.md and in ORM model definitions — specifying column(s), index type
(B-tree, composite, or partial), and the specific query each index supports

**Authorisation:**

• Every endpoint that fetches a resource by ID must
verify that the requesting user owns or has explicit permission for that
specific resource — authentication alone is not sufficient; this is the Broken
Object Level Authorisation vulnerability and is the most common API security
flaw in AI-generated code

• Middleware must extract and validate store_id,
customer_id, role, and scope claims from the JWT payload — all database queries
must be scoped to the authenticated customer’s store context; a customer must
not be able to access cart, order, or voucher records belonging to another
customer, and store-level operations such as the abandonment scan and analytics
must be restricted to the authenticated store_id only

• Admin-only endpoints (scan trigger, analytics summary)
must be tested with a non-admin token and must return 403 with a
machine-readable error code — not a generic 401 or 500
```

## 10. Inventory model and service request

```text
Generate an Inventory model and an Inventory
service with create, update-stock, get-by-product, and delete functions. Use a
database.
```

## 11. Inventory review document request

```text
Create REVIEW.md — A structured code review documenting every issue found above:
-	What the issue is, where it is, its severity (Critical / High / Medium / Low), and its impact in a retail context
-	How you detected it — your review process, including where Copilot assisted and where your own engineering judgment was required
-	The fix you applied or recommend
-	Security-specific review: check for JWT algorithm pinning, mass assignment vulnerabilities, token expiry validation, and Broken Object Level Authorisation gaps in any authentication-related code generated
-	A mandatory closing section: “Issues Copilot Introduced That Required Human Judgment” — what did the AI get wrong that only an engineer would catch? This section must include at least one retail-specific risk that Copilot introduced — for example: use of float instead of decimal for unit prices or cart totals, a missing stock level constraint that would allow negative inventory, a voucher code exposed in a public-facing API response payload, customer PII (email or name) logged in plain text in a notification event, or a missing store_id scope check that would allow cross-store inventory access
```

## 12. Inventory sync production rewrite request

```text
Review and Rewrite the Inventory Sync module to support production standards
```

## 13. Inventory remediation and security requirements

```text
Review REVIEW.md file and work on Remediation and make sure it will below points also

•	Remediated Inventory Sync Model (using ORM, decimal for monetary values)
•	Inventory Sync Repository (data access layer — ORM-based, no raw database driver)
•	Remediated Inventory Sync Service (business logic only — no SQL, no DB imports, no direct model binding, DTO whitelisting visible)
•	JWT middleware showing algorithm pinning (RS256/HS256) and token expiry validation
```

## 14. SPEC.md creation request

```text
now i have to work on '**Cart Abandonment Recovery Feature **'

and below is details , but before that create SPEC.md  for 'Build the Cart Abandonment Recovery system on top of the remediated Inventory Sync module, using SPEC.md as your implementation blueprint.'


**Abandoned Cart Model**

A cart record with fields:
customer ID, session ID, list of items (product ID, quantity, unit price —
stored as decimal), cart total (decimal — never float), currency, abandonment
timestamp, recovery status (pending / notified / converted / expired), voucher
code (nullable), and recovery timestamp (nullable). Model must include a
version integer field for optimistic concurrency control.



**Cart Abandonment Detection Service**

Business logic that:

•       
Queries for carts inactive for 30 or more minutes with
status pending checkout — query must use the declared composite index

•       
Processes carts in configurable batches; commits each
batch independently; tracks the last processed cursor for restartability

•       
Marks qualifying carts as abandoned and records the
abandonment timestamp

•       
Evaluates cart value against a configurable voucher
threshold

•       
Generates a one-time discount voucher for high-value
abandoned carts

•       
Triggers a notification event (email or push) with cart
summary and optional voucher

•       
Handles the case where a customer has multiple
abandoned carts (most recent takes priority)



**Recovery Tracking Service**

Business logic that:

•       
Listens for order completion events and matches them
against open abandoned cart records

•       
Verifies that the completing customer ID matches the
cart owner before marking recovery — Broken Object Level Authorisation check at
the service layer

•       
Marks a recovery as successful (converted) when the
customer completes purchase within 24 hours

•       
Marks recoveries as expired after the 24-hour window
lapses without conversion

•       
Logs all state transitions for analytics



**API Endpoints**

•       
POST /scan/abandoned-carts — Trigger abandonment
detection scan (admin role only; returns 403 for non-admin tokens with
machine-readable error code)

•       
GET /carts/:id/recovery — Get recovery status for a
specific cart (must verify requesting user owns this cart — Broken Object Level
Authorisation; return 403 if not)

•       
GET /analytics/recovery — Get recovery analytics
summary for a date range using cursor-based pagination (admin role only;
returns 403 for non-admin tokens)



**Tests — Minimum 8 test cases covering:**

•       
Cart correctly flagged as abandoned after 30 minutes of
inactivity

•       
Voucher generated when cart total exceeds the
configured threshold

•       
No voucher generated when cart total is below the
threshold

•       
Recovery marked as converted when order completed
within 24 hours

•       
Recovery marked as expired when 24-hour window lapses

•       
Customer with multiple abandoned carts: only the most
recent triggers a recovery workflow

•       
Broken Object Level Authorisation: customer A’s token
attempting to access customer B’s cart recovery status must return 403, not 200

Admin-only endpoint: non-admin token calling the scan trigger endpoint
must return 403 with a machine-readable error code
```

## 15. Abandoned Cart Model request

```text
create the Cart Abandonment Recovery System :

•	Generate Abandoned Cart Model (decimal for all monetary fields, version field, ORM with index declarations)
```

## 16. Cart Abandonment Detection Service request

```text
work on Cart Abandonment Detection Service and it should 30-minute threshold, configurable batch scan with cursor restartability, voucher threshold evaluation using decimal
```

## 17. Recovery Tracking Service request

```text
work on Recovery Tracking Service and it should cover 24-hour window, Broken Object Level Authorisation check, conversion and expiry logic
```

## 18. API endpoint request

```text
make below API endpoints we have
•	API Endpoints : cursor-based pagination on analytics endpoint, 403 response for non-admin on scan trigger
```

## 19. Test coverage and prompt documentation request

```text
for **— Prompt Engineering Documentation** Requirements is:
•	Must show at least 2 different Copilot features used across the chain (e.g. Ask Mode, Edit Mode, Agent Mode, #file, @workspace, /tests)
•	Must show at least 3 different prompting techniques (Specificity, Decomposition, Few-shot, Constraint-based, Role-based, Iterative Refinement)
•	At least 1 prompt must include an explicit security or retail constraint (JWT pinning, DTO whitelisting, Broken Object Level Authorisation, cursor pagination, decimal currency, store_id scoping)
```

# Prompt Engineering Documentation

| Prompt # | Prompt Text | Copilot Feature Used | Technique Applied | Why This Approach |
|---|---|---|---|---|
| 1 | Create the RetailEdge repository structure. | Agent Mode | Decomposition | Separates repository setup from later feature implementation. |
| 2 | Add case-study information and engineering goals to `README.md`. | Agent Mode + Edit Mode | Specificity | Gives the implementation a persistent source of project requirements. |
| 3 | Define architecture, JWT, DTO, BOLA, pagination, and testing rules in Copilot instructions. | Agent Mode + `#file` context | Constraint-based + Decomposition | Explicit constraints prevent mass assignment, weak JWT validation, offset pagination, and cross-object access. |
| 4 | Create `SPEC.md` for the complete Cart Abandonment Recovery system. | Agent Mode | Decomposition + Iterative Refinement | Converts the feature brief into a traceable implementation blueprint. |
| 5 | Generate the Abandoned Cart model with `decimal` totals and unit prices, an integer version, ORM mappings, and composite indexes. | Agent Mode + Edit Mode | Specificity + Constraint-based | Required decimal currency prevents binary floating-point errors; ORM indexes support required queries. |
| 6 | Implement 30-minute abandonment detection with configurable batch size, restartable cursor, and decimal voucher threshold. | Agent Mode + Edit Mode | Decomposition + Iterative Refinement | Splits detection into query, batch transaction, cursor, voucher, notification, and test concerns. |
| 7 | Implement order conversion and expiry within 24 hours, including a service-layer BOLA customer-owner check. | Agent Mode + Edit Mode | Constraint-based + Iterative Refinement | Makes ownership verification and expiry mutually exclusive and testable. |
| 8 | Add the scan and analytics APIs, with cursor pagination and machine-readable `403 ADMIN_ROLE_REQUIRED` responses for non-admin users. | Agent Mode + Ask/Chat Mode | Specificity + Constraint-based | Keeps authorization explicit and makes the HTTP contract mechanically testable. |
| 9 | Add BOLA and admin-only tests, then run `/tests` and address failures. | Agent Mode + `/tests` + Edit Mode | Test-driven refinement + Iterative Refinement | Verifies Customer A cannot access Customer B's cart and non-admin scan requests receive `403`. |

## Prompt Engineering Techniques Demonstrated

- **Specificity:** Names exact fields, thresholds, status values, indexes, DTOs, and HTTP response codes.
- **Decomposition:** Separates model, ORM, detection, recovery, API, and tests into independent implementation steps.
- **Constraint-based:** Requires `decimal` currency, JWT pinning, DTO allowlisting, BOLA checks, and cursor pagination.
- **Iterative Refinement:** Adds migrations, fixes SQLite translations, strengthens cursor behavior, and then adds explicit authorization tests.
- **Role-based context:** Frames Copilot as a .NET/C# implementation engineer working under RetailEdge architecture and security rules.

## Security and Retail Constraints Demonstrated

At least one prompt explicitly required each major constraint relevant to the
implementation:

- Use `decimal`, never `float` or `double`, for cart totals and item prices.
- Use an integer optimistic-concurrency version.
- Implement Broken Object Level Authorisation: Customer A must not access
  Customer B's recovery resource.
- Restrict the scan and analytics endpoints to the admin role.
- Implement machine-readable `403` errors for non-admin requests.
- Use cursor-based analytics pagination rather than offset pagination.

## 20. PR description artifact request

```text
refer above PR session and create PR_DESCRIPTION.md with:

• Summary of what was built and why
• AI Tool Disclosure: which Copilot features you used, where you accepted AI output vs overrode it, and your estimate of AI-generated vs hand-written code percentage
• Security note: how you ensured JWT algorithm pinning, DTO whitelisting, token expiry validation, and Broken Object Level Authorisation were correctly implemented and not introduced incorrectly by Copilot
• Testing coverage and known gaps
• At least 1 genuine risk or trade-off in your implementation
• Self-review checklist you ran before submitting
```

## 21. Scope change impact analysis request

```text
After completing the initial build, assess this scope change:

Add a second recovery touchpoint: if the customer does not respond to the first notification within 6 hours, send a follow-up with a higher discount. Also feed abandoned cart data into the recommendation engine so suggested products appear in the recovery email.

Produce IMPACT_ANALYSIS.md covering required data-model, service, and API changes; effort and risk; test updates; dependencies and integrations; recommendation to accept as-is, phase, or negotiate scope; retail business impact including voucher margin and email opt-out obligations; recommendation-engine data contracts and PII/data-sharing agreement; migration plan; and how GitHub Copilot assisted and was validated.
```

## 22. Tool strategy reflection request

```text
Create TOOL_STRATEGY.md documenting Copilot usage and critical analysis across the assessment.

Include:
• Feature Usage Log: minimum 6 entries covering at least 4 features
• Scenario Responses for legacy cart analysis, cursor pagination, decimal discount verification, persistent team instructions, JWT middleware review, and repository boilerplate generation
• Security Review: at least 3 AI-specific security risks, including JWT algorithm confusion, mass assignment through missing DTO whitelisting, and Broken Object Level Authorisation
• Limitations Encountered: 3 real incorrect, incomplete, or inappropriate Copilot outputs
```

## 23. Architecture documentation request

```text
Create ARCHITECTURE.md with 10-15 lines covering:
• relationship between Inventory Sync and Cart Abandonment Recovery in the RetailEdge microservices platform
• layered architecture and data flow from inactivity detection through notification dispatch and conversion tracking
• cursor pagination and batch processing for abandonment scan scale
• suitability for web, mobile, and in-store channels
• eventual consistency between cart, inventory, and notification services
• key design decisions and trade-offs
```

## Post-Generation Corrections

| # | Copilot Output | Problem | Correction |
|---|---|---|---|
| 1 | Inventory used a global product key. | It did not distinguish stock at separate stores. | Added `StoreId` and the unique `(StoreId, ProductId)` index, plus store-scoped operations. |
| 2 | JWT verification did not include all lifetime requirements. | Copilot could accept expired, unsigned, or future-issued tokens. | Pinned RS256/HS256 and added `exp`, numeric `iat`, `nbf`, issuer, audience, and signature validation. |
| 3 | Inventory and cart money used or could use floating-point types. | Binary floating point could introduce retail rounding errors. | Used `decimal` and exact SQLite decimal storage. |
| 4 | Abandoned checkout carts were represented as cancelled. | The status did not match the retail abandonment workflow. | Added `CartCheckoutStatus.Abandoned` and state-transition logic. |
| 5 | SQLite/LINQ used enum and `DateTimeOffset` operations unsupported by the provider. | Focused tests failed with EF Core translation/order-by errors. | Added EF integer-property comparisons and UTC-tick fields for keyset filtering and ordering. |
| 6 | The API owner check was not covered by an HTTP-level test. | Service-only ownership logic did not prove a `403` response. | Added Customer A/Customer B API coverage asserting `403 CART_ACCESS_DENIED`. |
| 7 | The scan cursor could be inserted twice. | The restart cursor raised a SQLite unique-key conflict. | Added cursor existence detection before `Add`. |
| 8 | Prompt documentation lacked explicit Copilot features and techniques. | It did not satisfy the prompt-engineering documentation format. | Added a feature/technique table and explicit security constraints. |
