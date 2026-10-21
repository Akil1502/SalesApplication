# English and Tamil

Each person picks English or தமிழ் in the account menu (or on the sign-in page). The choice is saved with their other
settings on the server. Printed documents and the reminders copied for customers follow each customer's (or
supplier's) own document language instead: English, Tamil or both.

## How the app is translated

**The English text in the code is the key.** In the React app:

```tsx
import { msg, t, tc, tn } from '@/lib/i18n'
import { T } from '@/lib/i18n/T'

t('Save')                                         // "Save" / "சேமி"
t('Saved {name}', { name: customer.name })        // fill in {placeholders}; never glue sentences together
tn(count, '{count} invoice', '{count} invoices')  // singular or plural; {count} is filled in
tc('status', 'Open')                              // same English, a different Tamil in this context
const LABELS = { Draft: msg('Draft') }            // outside a component: mark it here...
{t(LABELS[status])}                               // ...and translate it where it's shown
<T text="Press {key} to search" values={{ key: <kbd>/</kbd> }} />  // parts that aren't plain text
```

- The Tamil is in `frontend/src/locales/ta.json`, keyed by the English. A text with no Tamil yet shows in English.
- `npm test` fails (src/test/i18n.test.ts) when something shown on screen isn't marked, when a marked text has no
  Tamil, when the Tamil drops a {placeholder}, or when ta.json still has text the app no longer uses.
- `node scripts/i18n.ts [folder]` (in frontend) lists what's missing, for the whole app or one folder.
- Never call `t()` at module level. Changing language re-renders the whole app, so `t()` inside a component is
  always current. Text defined outside one is marked with `msg()` and translated with `t()` where it's shown. The
  table's column headers and the segmented control's labels are translated by those components, so `msg()` is enough
  there.
- Don't translate data: customer and product names, codes, numbers and anything else people typed in.
- Dates use `formatBusinessDate`, `formatDate`, `formatDateTime` or `formatDateParts` from `@/lib/format`, which
  name the month in the language on screen. Amounts and quantities keep digits 0–9 and Indian grouping in both.

**The API answers in the same language.** Every request carries `Accept-Language`, and the API sets the request's UI
culture from it (only the UI culture: numbers and dates in files don't change). Server text is keyed by its English
too, in `backend/src/SalesApp.Core/Localization/ta.json`, where `{0}`, `{1}` are values the server fills in:

- Text is written in English and translated where it leaves the server, with `L.T(text)`: error responses
  (`ApiError` does it), notifications, reports, the audit trail, bill-matching notes and so on. A template such as
  "Invoice {0} was not found." matches any invoice number, so messages built with interpolation, and text saved in
  English earlier (notifications, the audit trail), still read in Tamil.
- Exception and DataAnnotations messages are collected by the tests automatically; mark any other text the server
  shows with `L.Msg(...)` (or `L.Show(...)` for text sent straight back). Write whole sentences: a word chosen inside a
  `{hole}` stays English in a Tamil sentence, and a test catches that too.
- `dotnet test` in backend fails (`ServerTextTests`) when a server text has no Tamil, when the Tamil drops a
  placeholder, or when ta.json has text the server no longer writes.
- The AI is told the language: the assistant and Ask your data answer in Tamil for a Tamil user, drafts for a customer
  follow the customer's document language, and the nightly insights are written in English and Tamil in one call.

**Printed documents** use `<DocText text="…" />`, `<DocField label="…">` and `docText(language, '…')` from
`features/sales/print`, which follow the document's language (the customer's or supplier's, or the company default),
not the screen's. A both-language document puts the Tamil under each English label.

**Review.** From the repo root, `dotnet run scripts/translations.cs -- export` writes an Excel sheet
(Id | Where | English | Tamil now | Correction | Comment) to `translations-review/` for a Tamil-speaking reviewer, with
a Read me and the Glossary. `dotnet run scripts/translations.cs -- import <file.xlsx>` takes their corrections back
into both ta.json files, refusing any that drop a {placeholder}. Then run the tests and look at the screens in Tamil.

## Tamil style

- **Simple written Tamil** that anyone who reads Tamil follows. No spoken-style English words in Tamil script
  (not இன்வாய்ஸ், பேலன்ஸ்).
- **Tax, bank and code terms stay in English letters**: GST, CGST, SGST, IGST, UTGST, GSTIN, HSN, SKU, PAN, IFSC,
  UPI, NEFT, RTGS, PO, PDF, AI, Excel, WhatsApp, Claude. Units stay as kg, m, pcs. Add a Tamil case ending with a
  hyphen when needed: GSTIN-ஐ, Excel-இல், UPI-மூலம்.
- **Buttons and menu items** are short commands: சேமி, ரத்துசெய், நீக்கு, திருத்து, மூடு.
- **Sentences to the user** are polite: உங்கள்…, …உள்ளிடுங்கள், …முயலுங்கள்.
- **Headings** are nouns, like the English.
- Keep ₹, digits, and the app's punctuation (… · —).

## Glossary

Use these words everywhere, so the same thing has the same name on every screen.

| English | Tamil |
|---|---|
| Dashboard | முகப்பு |
| Customer, customers | வாடிக்கையாளர், வாடிக்கையாளர்கள் |
| Supplier, suppliers | வழங்குநர், வழங்குநர்கள் |
| Product, products | பொருள், பொருட்கள் |
| Category | வகை |
| HSN code | HSN குறியீடு |
| Order, sales order | ஆர்டர், விற்பனை ஆர்டர் |
| Quotation | விலைப்புள்ளி |
| Invoice, tax invoice | விலைப்பட்டியல், வரி விலைப்பட்டியல் |
| Payment, payments | கட்டணம், கட்டணங்கள் |
| Payment received | கட்டணம் பெறப்பட்டது |
| Record payment | கட்டணத்தைப் பதிவுசெய் |
| Credit note | வரவுக் குறிப்பு |
| Debit note | பற்றுக் குறிப்பு |
| Return, returns | திருப்பம், திருப்பங்கள் |
| Refund | பணத்தைத் திருப்பித் தருதல் |
| Stock | இருப்பு |
| Low stock / out of stock | குறைந்த இருப்பு / இருப்பு இல்லை |
| Reorder level | மறு ஆர்டர் நிலை |
| Stock receipt | இருப்பு வரவு |
| Adjustment | சரிசெய்தல் |
| Opening stock | தொடக்க இருப்பு |
| Purchase, purchasing | கொள்முதல் |
| Purchase order | கொள்முதல் ஆர்டர் |
| Bill (from a supplier) | பில் |
| Receive goods | சரக்கைப் பெறு |
| Rates, price list | விலைகள், விலை அட்டவணை (not விலைப்பட்டியல், which is an invoice) |
| Amount | தொகை |
| Total | மொத்தம் |
| Subtotal | துணை மொத்தம் |
| Balance due | செலுத்த வேண்டிய நிலுவை |
| Outstanding | நிலுவை |
| Due date | செலுத்த வேண்டிய தேதி |
| Overdue | தவணை கடந்தது |
| Paid / partly paid / unpaid | செலுத்தப்பட்டது / பகுதி செலுத்தப்பட்டது / செலுத்தப்படவில்லை |
| Draft | வரைவு |
| Confirmed | உறுதிசெய்யப்பட்டது |
| Cancelled | ரத்துசெய்யப்பட்டது |
| Quantity, qty | அளவு |
| Rate, unit price | விலை, அலகு விலை |
| Discount | தள்ளுபடி |
| Taxable value | வரிக்குட்பட்ட மதிப்பு |
| Tax | வரி |
| Round-off | முழுமையாக்கல் |
| Place of supply | வழங்கல் இடம் |
| Credit limit | கடன் வரம்பு |
| Payment terms | கட்டண விதிமுறைகள் |
| Sales rep | விற்பனைப் பிரதிநிதி |
| Sales manager | விற்பனை மேலாளர் |
| Admin | நிர்வாகி |
| User, users | பயனர், பயனர்கள் |
| Role | பணிப்பங்கு |
| Permission, rights | அனுமதி, உரிமைகள் |
| Approve, approval | ஒப்புதல் அளி, ஒப்புதல் |
| Settings | அமைப்புகள் |
| Company settings | நிறுவன அமைப்புகள் |
| Report, reports | அறிக்கை, அறிக்கைகள் |
| Insights | நுண்ணறிவுகள் |
| Forecast | முன்கணிப்பு |
| Assistant | உதவியாளர் |
| Ask your data | உங்கள் தரவிடம் கேளுங்கள் |
| Smart order entry | AI ஆர்டர் பதிவு |
| Notifications | அறிவிப்புகள் |
| Audit trail | தணிக்கைப் பதிவு |
| Search | தேடு (button), தேடல் (noun) |
| Filter | வடிகட்டு |
| Saved views | சேமித்த காட்சிகள் |
| Favourites | பிடித்தவை |
| Recently viewed | சமீபத்தில் பார்த்தவை |
| Export for Excel | Excel-க்கு ஏற்றுமதி செய் |
| Print | அச்சிடு |
| Sign in / sign out | உள்நுழை / வெளியேறு |
| Email | மின்னஞ்சல் |
| Password | கடவுச்சொல் |
| Phone | தொலைபேசி |
| Address | முகவரி |
| State | மாநிலம் |
| City | நகரம் |
| PIN code | PIN குறியீடு |
| Notes | குறிப்புகள் |
| Financial year | நிதியாண்டு |
| This month / quarter | இந்த மாதம் / இந்தக் காலாண்டு |
| Today / yesterday / earlier | இன்று / நேற்று / முன்பு |
| Save / cancel / delete / edit | சேமி / ரத்துசெய் / நீக்கு / திருத்து |
| Add / new | சேர் / புதிய |
| Close / back / next / previous | மூடு / பின்செல் / அடுத்து / முந்தைய |
| Open | திற |
| Copy / copied | நகலெடு / நகலெடுக்கப்பட்டது |
| Try again / reload | மீண்டும் முயலுங்கள் / மீண்டும் ஏற்று |
| Confirm / submit | உறுதிசெய் / சமர்ப்பி |
| Light / dark | ஒளி / இருள் |
| Loading… | ஏற்றுகிறது… |
| Something went wrong | ஏதோ தவறு நடந்தது |
