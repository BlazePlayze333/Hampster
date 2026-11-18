DESKTOP HAMSTER - WHEEL + REST + GITHUB BUILD
Source package, not a precompiled EXE. Based on the tray-care/petting source
pasted in chat. No compilation or interactive Windows testing done here.

GITHUB
Extract ZIP. Upload its CONTENTS to the root of BlazePlayze333/Hampster,
replacing Program.cs and DesktopHamster.csproj. Include the hidden .github
folder: the workflow must be .github/workflows/build.yml. Do not upload ZIP
alone. If web upload skips that folder, use Add file > Create new file,
enter .github/workflows/build.yml, and paste the included workflow text.
Keep app.ico if the hamster project already uses it; no icon is replaced.

After committing to main/master the workflow runs. It can also be launched
from Actions > Build DesktopHamster > Run workflow on the default branch.
The workflow builds on a GitHub Windows runner and creates a GitHub Release
with ONE DesktopHamster.exe asset. Download that asset, not Source code ZIP.
The EXE includes the .NET runtime. Run locally after exiting the old hamster.
No need for local SDK/source if downloading the built EXE.
If releases fail, inspect Actions logs and ensure repository policy permits
workflow contents:write. No personal access token is required by this workflow.
Build success is not the same as visual testing.

No GitHub files have been changed on your behalf. No auto-updater/launcher is
included yet; future source commits trigger builds, but replacing the installed
EXE remains a manual download until an updater is added. Private repository
release downloads need GitHub sign-in; never embed a token in shared source.

WHEEL
Ctrl+Alt+W toggles the wheel at pointer. Drag its rim/base to move it.
He chooses it occasionally when energy >50 and its cooldown has elapsed.
He runs 18-32 seconds while wheel spokes rotate and paws animate.
Wheel drains 0.72 energy/sec versus 0.18 during other active behavior: 4x.
Finishing leads to a breather and a 45-80 sec cooldown before wheel reuse.
If energy falls below 25, he leaves, rests or heads to bed, and needs >50
before reusing the wheel. Moving/removing the wheel stops him safely.
Clicking him pets him and interrupts running. Ctrl+Alt+R calls him away.

REST
Random breaks become due after roughly 35-65 active seconds (first at 40).
They happen during wandering, not mid-bite or mid-pet. Breathers usually last
8-14 seconds; exhausted resting lasts longer. Rest restores 0.65 energy/sec;
bed sleeping restores 5/sec. Gentle breathing/closed eyes show rest.
Calling interrupts rest, with a 30-second grace period before another break.
Hidden/paused time now freezes activity timers and animation, not just motion.

EXISTING CONTROLS (all Ctrl+Alt)
F bowl; B bed; T toy; Q ball; E refill; R call; H hide/show; W wheel.
Objects draggable, hamster not. Tray hover for short summary, left/right click
for care/menu. No separate care window; petting, visible food consumption,
toy hopping, saved needs/food retained. No icon recognition or full physics.
Positions/visibility aren't persisted, and wheel isn't auto-spawned at startup.

LOCAL BUILD (optional)
Double-click Build-and-Run-Hamster.bat with .NET 10 SDK installed.
Or: dotnet publish -c Release -r win-x64 --self-contained true -o ReadyToRun
EXE is single-file. Existing files from older builds are not deleted by BAT.
Single-file .NET apps may extract native components to a temporary cache.
