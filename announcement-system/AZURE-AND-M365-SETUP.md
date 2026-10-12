# RGC – Put the server online (Azure) with Microsoft 365 sign-in

When you're done:
- the RGC server runs in **Azure**, at e.g. `https://rgc-announcements.azurewebsites.net`;
- **admins** sign in to the Admin Console with their Microsoft 365 account, including MFA;
- **staff PCs** identify each user by their 365 account. Each user signs in once per PC, and the read
  report shows their email.

Time needed: about 45 minutes. Cost: about **US$18/month** (App Service B1 + Azure SQL Basic).

You need: a Microsoft 365 / Entra **admin** account, and an **Azure subscription**. If you don't
have one, create it at portal.azure.com with the same work account.

---

## Part A – Register the app in Microsoft Entra (about 10 min)

Go to **https://entra.microsoft.com** → **Identity → Applications → App registrations**.

1. **New registration**
   - Name: `RGC Announcements`
   - Supported account types: **Accounts in this organizational directory only**
   - Redirect URI: leave empty → **Register**
   - From the Overview page, copy the **Application (client) ID** and the **Directory (tenant) ID**.
     You'll need both in Part B.

2. **Expose an API**
   - Next to *Application ID URI*, click **Add** → keep `api://<client-id>` → **Save**
   - **Add a scope**:
     - Scope name: `access_as_user`
     - Who can consent: **Admins and users**
     - Admin consent display name: `Access RGC Announcements`
     - Admin consent description: `Sign in to RGC Announcements`
     - **Add scope**

3. **App roles → Create app role**
   - Display name: `RGC Admin`
   - Allowed member types: **Users/Groups**
   - Value: `Admin` (exactly this)
   - Description: `Can send company announcements`
   - Tick *Do you want to enable this app role?* → **Apply**

4. **Authentication → Add a platform → Mobile and desktop applications**
   - Under *Custom redirect URIs*, add both of these. Replace `<client-id>` with your Application (client) ID.
     ```
     ms-appx-web://microsoft.aad.brokerplugin/<client-id>
     http://localhost
     ```
   - **Configure**
   - Further down, set *Allow public client flows* to **Yes** → **Save**

5. **API permissions → Add a permission → APIs my organization uses**
   - Search for `RGC Announcements` → **Delegated permissions** → tick `access_as_user` → **Add permissions**
   - Click **Grant admin consent for <your organisation>** → **Yes**. This means staff aren't asked
     to approve anything.

6. **Choose who can send announcements**
   - Go to **Identity → Applications → Enterprise applications** → `RGC Announcements` → **Users and groups**
   - **Add user/group** → pick yourself and any other admins. A group works too, e.g. "IT Team".
   - Role: **RGC Admin** → **Assign**

   Everyone else in the company can still sign in on their PC to *receive* announcements. Only people
   with the RGC Admin role can open the Admin Console.

---

## Part B – Create the server in Azure (about 20 min)

Go to **https://portal.azure.com**.

### B1. Database (Azure SQL)
1. **Create a resource → SQL Database**
   - Resource group: **Create new** → `rgc-announcements`
   - Database name: `RGC_Announcements`
   - Server: **Create new**
     - Name: e.g. `rgc-sql-bigc`. It must be unique and lowercase.
     - Location: **UAE North**, the closest region to Bahrain
     - Authentication: **Use SQL authentication**. Choose an admin login and a strong password,
       and write them down.
   - Want to use SQL elastic pool: **No**
   - Workload environment: **Development**
   - Compute + storage: **Configure database** → **Basic** → Apply
2. **Networking** tab:
   - Connectivity method: **Public endpoint**
   - *Allow Azure services and resources to access this server*: **Yes**
3. **Review + create → Create**. It takes a few minutes.
4. When it's ready, open the database → **Connection strings** → **ADO.NET (SQL authentication)**.
   Copy the string and replace `{your_password}` with the password you chose.

### B2. Web app (App Service)
1. **Create a resource → Web App**
   - Resource group: `rgc-announcements`
   - Name: e.g. `rgc-announcements`. This becomes `https://rgc-announcements.azurewebsites.net`.
   - Publish: **Code** · Runtime stack: **.NET 8 (LTS)** · Operating system: **Linux**
   - Region: **UAE North**
   - Pricing plan: **Basic B1**
   - **Review + create → Create**
2. Open the web app → **Settings → Environment variables**.
   - On the **App settings** tab, add each of these with **+ Add**:

     | Name | Value |
     |---|---|
     | `Jwt__SigningKey` | A long random text of 48+ characters. To generate one in PowerShell: `$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)` |
     | `Entra__TenantId` | your Directory (tenant) ID |
     | `Entra__ClientId` | your Application (client) ID |
     | `Authentication__AllowAgentKey` | `false` |
     | `Authentication__AllowLocalAdminLogin` | `true`. This keeps an emergency local admin account. Set it to `false` to use Microsoft 365 only. |
     | `BootstrapAdmin__Password` | A password for the emergency local `admin` account. Remove this setting after the first start. |

   - On the **Connection strings** tab, click **+ Add**:
     - Name: `RgcDatabase`
     - Value: the connection string from B1 step 4
     - Type: **SQLAzure**
   - **Apply → Confirm**
3. **Settings → Configuration → General settings**:
   - **Web sockets: On**. This is required for live popups.
   - **Always on: On**
   - **SCM Basic Auth Publishing Credentials: On**. This is needed for the GitHub deployment below.
   - **Save**

### B3. Deploy the server from GitHub
1. **Merge the pull request** into `main`. The deployment workflow only appears in GitHub once it's on `main`.
2. In the web app's **Overview**, click **Download publish profile**.
3. In GitHub, open `Ahmedsami997/RGC` → **Settings → Secrets and variables → Actions**.
   - **Secrets** tab → **New repository secret**:
     - Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
     - Value: open the downloaded file in Notepad and paste its entire contents
   - **Variables** tab → **New repository variable**:
     - Name: `AZURE_WEBAPP_NAME`
     - Value: your web app name, e.g. `rgc-announcements`
4. **Actions** → **Deploy RGC Server to Azure** → **Run workflow**. Wait for the green tick, about 3 minutes.
5. Test it. Open these in a browser:
   - `https://rgc-announcements.azurewebsites.net/api/health` should show `"status":"ok"`
   - `https://rgc-announcements.azurewebsites.net/api/auth/config` should show `"entraEnabled":true`

   The first start creates the database tables automatically.

From now on, every change merged to `main` that touches the server redeploys it automatically.

---

## Part C – Point the apps at the online server

**Admin Console:** open RGC Admin. Set the Server to `https://rgc-announcements.azurewebsites.net` and
click **Sign in with Microsoft 365**.

**Each staff PC**, as Administrator. The PC needs the .NET Desktop Runtime 8 (x64). Run this in the RGC-Agent folder:
```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\Install-Agent.ps1 -ServerUrl "https://rgc-announcements.azurewebsites.net"
```
No key is needed. The first time each user logs on, a small **"Sign in to receive company
announcements"** window appears. They sign in with their work email once, and after that it's silent.
On PCs joined to Entra (hybrid join), there's no prompt at all.

**Your own PC:** once the online server works, you can turn off the local one:
```powershell
Stop-Service "RGC Announcement Server"; Set-Service "RGC Announcement Server" -StartupType Disabled
```
You can also uninstall SQL Server Express from this PC; the online server doesn't need it.
