# Primafit ERP: New User Walkthrough

This guide explains Primafit ERP from a new user’s point of view: create an account, prepare the company, configure accounting, add customers and vendors, and record everyday transactions.

The exact menus you can see depend on your role and permissions. If a menu is missing, ask your administrator to grant access.

## The setup order at a glance

Complete setup in this order:

1. Register and sign in.
2. Complete the company profile.
3. Confirm the base and functional currency, then add trading currencies and exchange rates.
4. Generate fiscal periods.
5. Configure account types and segments.
6. Build the segmented chart of accounts and transaction accounts.
7. Configure tax and withholding tax.
8. Add customers and vendors.
9. If you sell or buy stock, set up items, units of measure, warehouses, and item categories.
10. Start entering transactions and send them for GL review.

Do not begin with invoices or payments before the company, periods, currencies, and accounts are ready. Those transactions depend on that setup.

## 1. Register and sign in

1. Open Primafit ERP and select **Create account** on the login screen.
2. Enter the company name and official company email.
3. Choose the company structure.
4. Enter your first name and last name.
5. Enter your login email, password, and password confirmation.
6. Submit the registration form.
7. Return to the login screen and sign in with your login email and password.

After sign-in, Primafit opens the dashboard. If the company profile is incomplete, the top of the screen displays **Your organization profile is incomplete!** Select **Complete Setup Now**.

## 2. Complete the company profile

Open **Settings → Company Details** (the page is also available as **Company Details** in the settings area).

Complete the following sections:

- **Identity:** company logo, company name, registration number, and tax identification number.
- **Addresses:** physical address and postal address.
- **Contact & Details:** company email, website, country, and company type.
- **Financials:** fiscal start date, fiscal end date, base currency, and functional currency.

Select **Save Company**. When editing an existing record, the button is **Update Company**.

### What the financial currency fields mean

- **Base currency:** the currency used as the company’s accounting base.
- **Functional currency:** the currency used for the company’s main operating and reporting activity.
- **Trading currency:** another currency used for a customer, vendor, invoice, or payment.

Use consistent currency codes, such as `NGN`, `USD`, `GBP`, or `EUR`. Confirm the company’s fiscal dates before generating periods.

## 3. Add currencies and exchange rates

Open **Settings → Currency Setup**.

### Add a currency

1. Select **Define New Currency** or the equivalent add button.
2. Enter the **Currency Code**, for example `USD`.
3. Enter the **Currency Name**, for example `US Dollar`.
4. Save the currency.

### Add an exchange rate

1. Open the exchange-rate section on the same page.
2. Enter the **Effective Date**.
3. Select the **Trading Currency**.
4. Confirm the **Base Currency Target** shown by the company setup.
5. Enter the **Exchange Rate Multiplier**.
6. Save the rate.

Add a new rate whenever the rate changes. The effective date matters: it tells Primafit which rate applies to a transaction date.

## 4. Generate fiscal periods

Open **Settings → Accounting Periods**.

1. Confirm that the company fiscal start and end dates are correct.
2. Select **Generate Periods**.
3. Confirm the prompt.
4. Review the generated period names, start dates, and end dates.

Primafit generates missing periods for the fiscal year. Existing periods and their transactions are not replaced.

Use the **Open/Closed** control to close a period after its work is complete. Keep a period open while users still need to post transactions into it.

## 5. Configure account types and segments

### 5.1 Account types

Open **Finance & GL → Setup → Account Types**.

Account types classify accounts, such as assets, liabilities, equity, income, and expenses. Review the available types before creating accounts. Use the type that matches the account’s accounting purpose because Primafit uses account types when it filters accounts for tax and transaction mappings.

### 5.2 Main accounts / segments

Open **Finance & GL → Setup → Segments**.

Segments are the building blocks of a segmented account code. For example, a company may use:

- Segment 0: Main Account
- Segment 1: Department
- Segment 2: Branch
- Segment 3: Cost Centre

The names are configurable. Segment 0 is required. Turn on only the optional segments your company will actually use.

To configure segments:

1. Enter the name for Segment 0.
2. Name each optional segment you want to use.
3. Mark the relevant segments as active.
4. Select **Save Config**.
5. Move to the segment values area.
6. Select a segment.
7. Add values manually with a **Code** and **Description**, or import the values using the CSV import option.
8. Save each value.

Example values:

| Segment | Code | Description |
|---|---:|---|
| Main Account | 1000 | Cash and Bank |
| Department | 010 | Sales |
| Branch | LAG | Lagos |

Use short, consistent codes. Avoid changing a code after transactions have been posted.

## 6. Build the chart of accounts

Primafit has a segmented chart-of-accounts workflow. Open **Finance & GL → Setup → Chart of Accounts**.

### 6.1 Create the main/header account

1. Select the main-account or header-account option.
2. Enter the account code and account name.
3. Select the appropriate account type.
4. Save the account.

The header is the parent/main account. Examples include Cash, Accounts Receivable, Sales Revenue, Accounts Payable, VAT Payable, and Office Expenses.

### 6.2 Create a transaction account

1. Select **New Transaction Account** or **Build Account**.
2. Select the required Segment 0 value.
3. Select values for every active optional segment.
4. Confirm or enter the account code.
5. Enter the account description/name.
6. Select the account type when requested.
7. Choose whether the account is available for posting, where that option is shown.
8. Save the account.

The account must be complete before it can be used in a transaction. If Primafit says an active segment is missing, return to the form and select a value for every active segment.

### 6.3 Check the accounts

Search the chart of accounts by account code or description. Confirm that you have at least:

- A cash or bank account.
- A customer receivables account.
- A vendor payables account.
- A sales or income account.
- A purchase, inventory, or expense account.
- A tax liability account for output VAT/sales tax.
- A tax asset account for input VAT/purchase tax.
- A withholding-tax payable account, if withholding tax is used.

## 7. Configure tax and withholding tax

### 7.1 Tax / VAT

Open **Settings → Tax Setup**.

1. Select **Add Tax**.
2. Enter a **Tax Code**, such as `VAT-7.5`.
3. Enter the **Tax Name**, such as `Value Added Tax`.
4. Enter the **Percentage (%)**.
5. Select the mapped **GL Account (Liability)**. This is the account credited when a sales invoice is posted.
6. Select **Save Tax**.

For purchase tax, make sure the purchasing route is mapped to an input-tax asset account where required. If the system or transaction route offers a specific input-tax account, use the account approved by your accountant.

### 7.2 Withholding tax

Open **Settings → Withholding Tax Setup**.

1. Select the option to create a new withholding tax.
2. Enter the withholding-tax **Name**, such as `WHT 5%`.
3. Enter the **Percentage Value (%)**.
4. Save the rate.
5. Configure the default GL account if Primafit displays **Configure Default GL Account**.

Primafit applies withholding tax to the invoice net amount before VAT. In purchasing, withholding tax is attached to an invoice and is withheld when the vendor payment is posted. Explain the rate and account mapping to your accountant before using it in live transactions.

## 8. Add customers

Open **AR & Sales → Setup → Customer Setup**.

1. Select the add/create customer option.
2. Enter the **Customer Name**.
3. Select a **Customer Group**, if your company uses groups.
4. Enter email, phone, and address.
5. Select the customer’s **Trading Currency**.
6. Save with **Save Profile**.

The customer’s currency is important. It controls which currency is available when entering invoices and receipts. Use the customer’s correct email if you want to send invoices or receipts from the system.

## 9. Add vendors

Open **AP & Procurement → Setup → Vendor Setup**.

1. Select the add/create vendor option.
2. Enter the vendor **Name**.
3. Select a **Vendor Group**, if applicable.
4. Enter email, phone, and address.
5. Select the vendor’s **Trading Currency**.
6. Select the **Payables Account (Liability)**, or leave the system default if your administrator has configured one.
7. Save with **Save Vendor**.

## 10. Optional stock setup before buying or selling items

If your transaction involves inventory, open **Inventory → Setup** and configure:

- Item Categories.
- Items & Services.
- Units of Measure.
- Warehouses.

Create the item, its unit of measure, selling or purchase price, and warehouse before entering an order. For service-only transactions, use a direct invoice or direct bill instead of an inventory order.

## 11. The transaction review lifecycle

Many Primafit transactions follow this pattern:

1. Create the document.
2. Save it as a draft if you are not ready to submit it.
3. Submit it for GL review.
4. A reviewer approves/posts it, or returns it with a rejection reason.
5. If rejected, choose **Edit & Resubmit**, correct the document, and submit it again.
6. After posting, the document is read-only and appears as **Posted**.

Transactions in **In Review**, **Staged**, or **Ready** status are not the same as fully posted transactions. Check the GL review/batches area and wait for the reviewer to post them before relying on financial reports.

## 12. Record a customer sale

### Option A: Sales order / item sale

Use this when selling items or services through the sales workflow.

1. Open **AR & Sales → Transaction → Sales Orders**.
2. Select **New** or create a new sales document.
3. Select the customer.
4. Select the warehouse if the sale uses stock.
5. Enter the transaction date.
6. Select the tax/VAT rate, or choose **No Tax**.
7. Add each item or service, unit of measure, quantity, and price.
8. Review discounts, tax, exchange rate, and total.
9. Save the order or submit the invoice for GL review, depending on the document status.
10. If stock needs to leave the warehouse, complete the shipment step from the sales workflow.

### Option B: Direct invoice

Use this for a service or revenue invoice that does not use inventory items.

1. Open **AR & Sales → Transaction → Direct Invoice**.
2. Select **Create Invoice**.
3. Select the customer and invoice date.
4. Select the currency and confirm the exchange rate.
5. Select a tax/VAT rate if applicable.
6. Add a line description, quantity, and unit price.
7. Add any discount as a percentage or flat amount.
8. Review the subtotal, tax, and total.
9. Select **Save Draft** if you need to finish later.
10. Select **Submit Invoice to GL Queue** when the invoice is ready.

After review, the invoice shows **Posted (Read-Only)**. You can download or email the invoice where those options are available.

### Receive the customer’s payment

1. Open **AR & Sales → Transaction → Customer Receipt**.
2. Select **Receive Payment** or create a new receipt.
3. Enter the date received.
4. Select the customer.
5. Select the customer invoice/order to apply the payment to.
6. Confirm currency and exchange rate.
7. Enter the amount received and the ledger/base amount.
8. Review the amount being applied to the invoice.
9. Select **Submit Payment for GL Review**.
10. Correct and resubmit if the receipt is rejected; otherwise wait for it to be posted.

## 13. Record a vendor purchase

### Purchase order and invoice

1. Open **AP & Procurement → Transaction → Purchase Orders**.
2. Select **New PO**.
3. Choose the document type: request, purchase order, or direct invoice, as appropriate.
4. Select the vendor and order date.
5. Select currency and confirm the exchange rate.
6. Select VAT/tax if applicable.
7. For an invoice document, select withholding tax if applicable. Primafit shows the withholding amount and net amount payable.
8. Add items, descriptions, quantities, units, and unit costs.
9. Review discounts, tax, withholding tax, and totals.
10. Save the request or purchase order, or select **Submit Invoice for GL Review** when submitting a vendor invoice.

For stock purchases, record the receipt of goods using **Receive Goods** or the goods-receipt workflow. If only part of the order arrives, use **Receive Remainder** when the next delivery arrives.

### Pay the vendor

1. Open **AP & Procurement → Transaction → Vendor Payment**.
2. Select **Make Payment**.
3. Select the vendor and the vendor bill.
4. Confirm the payment currency and exchange rate.
5. Enter the amount being paid.
6. Review any withholding amount and the net cash payment.
7. Select **Submit Payment for GL Review**.
8. Wait for the payment to be approved and posted. A rejected payment can be opened with **Edit & Resubmit**.

## 14. Enter a manual journal

Use a journal for adjustments such as depreciation, accruals, corrections, or opening balances.

1. Open **Finance & GL → Transaction → Journal Entry**.
2. Select **New Journal Batch**.
3. Enter the posting date.
4. Enter a batch description or purpose.
5. Create the batch.
6. For each line, select the GL account, enter the reference/line memo, and enter either a debit or a credit.
7. Add enough lines for total debits to equal total credits.
8. Check the balance shown in the workspace.
9. Select **Lock Batch** when the entries are ready for review.
10. Select **Submit for GL Review**.

If a reviewer rejects the batch, read the rejection reason, unlock or edit the batch, correct the lines, and select **Resubmit for GL Review**.

## 15. Check your work

After transactions are posted, use:

- **Finance & GL → Reports → Ledger Report** to inspect account activity.
- **Trial Balance** to confirm debits and credits.
- **Profit & Loss** to review income and expenses.
- **Balance Sheet** to review assets, liabilities, and equity.
- **AR customer transactions, Age Analysis, and Customer Statement** to review customers.
- **Vendor Statement, Aging Report, and Vendor Payment History** to review suppliers.
- **Cashbook Report** to review cash movements.

If a transaction is missing from a report, first check its status. Draft, staged, in-review, or rejected transactions may not be included until the GL review process is complete.

## Common beginner mistakes

- Creating a customer or vendor before adding the required trading currency.
- Generating fiscal periods before correcting the fiscal start and end dates.
- Creating a tax rate without mapping the correct GL account.
- Selecting a withholding rate without confirming the withholding payable account.
- Leaving an active segment blank when building a transaction account.
- Using a direct invoice when the transaction should affect inventory.
- Treating **In Review** as **Posted**.
- Trying to edit a posted document instead of creating a correction, credit note, debit note, or adjustment.
- Posting a customer receipt without selecting the correct invoice.
- Posting a vendor payment without checking withholding tax and the net amount paid.

## A simple first-day practice exercise

For a safe test run, create one test customer and one test vendor, then:

1. Create a small direct sales invoice.
2. Submit it for GL review and confirm that it appears in the invoice list.
3. Create a customer receipt against that invoice.
4. Create a small vendor invoice with a test item or service.
5. Attach a withholding rate if your company uses one.
6. Submit a vendor payment.
7. Review the customer statement, vendor statement, ledger, trial balance, and cashbook after the documents are posted.

This gives a new user the full flow: setup → invoice → receipt → vendor bill → payment → reports.
