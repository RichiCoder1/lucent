# Start authoring

Open the folder containing your Lucent project. For a new application, follow the
[coordinated starter template guide](https://github.com/RichiCoder1/lucent/blob/main/docs/TEMPLATES.md)
and keep the SDK and package versions pinned to the same release set.

Review Workspace Trust before enabling language services: project evaluation
executes trusted build tooling. Syntax highlighting and static environment checks
remain available in Restricted Mode.

Run **Lucent: Select Project** and choose a `.csproj` inside its owning workspace
folder. This writes the folder's `lucentLui.projectPath` setting and starts language
services. In a multi-root workspace, choose the owner explicitly. Removing that
folder stops its services; another folder is not selected automatically.

Open a `.lui` file and inspect the **Lucent** status item. If the project needs
different tools, use **Install Matching Language Tools** to reuse a verified cache
or explicitly download an approved release. Offline, use **Import Server Archive**
with an approved server ZIP. Installation preserves the running server until you
choose to restart. The extension does not change your project's package pins.

An absolute `lucentLui.serverPath` is available for advanced development setups.
The extension verifies the server's identity and the project's compiler before
starting semantic services. Mismatch details appear in **Problems** and the status
item; server diagnostics appear under **Output → Lucent LUI**.
