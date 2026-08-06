namespace VL.MCP;

/// <summary>
/// The friendly placeholder shown in the CEF chat window while Open WebUI starts.
/// Served by the bridge at /chat; auto-redirects to the real chat once it answers.
/// </summary>
internal static class ChatPlaceholderPage
{
    public static string Html(string status)
    {
        // The page polls the bridge's /api/chat/status and redirects to Open WebUI
        // once it's up. `status` is currently unused in the markup but kept for the API.
        return """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>vvvv MCP Chat</title>
<style>
  :root { color-scheme: dark; }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    height: 100vh; display: flex; flex-direction: column;
    align-items: center; justify-content: center;
    background: radial-gradient(ellipse at 50% 40%, #1a1d24 0%, #0c0e12 70%);
    font-family: "Segoe UI", system-ui, sans-serif; color: #cfd6e4;
    overflow: hidden;
  }
  .logo {
    width: 84px; height: 84px; border-radius: 20px;
    background: linear-gradient(135deg, #5b8cff 0%, #9b5bff 100%);
    display: flex; align-items: center; justify-content: center;
    font-size: 40px; font-weight: 700; color: white;
    box-shadow: 0 0 60px rgba(91,140,255,.45);
    animation: pulse 2.2s ease-in-out infinite;
    margin-bottom: 34px;
  }
  @keyframes pulse {
    0%, 100% { transform: scale(1);    box-shadow: 0 0 40px rgba(91,140,255,.35); }
    50%      { transform: scale(1.06); box-shadow: 0 0 70px rgba(155,91,255,.55); }
  }
  h1 { font-size: 22px; font-weight: 600; letter-spacing: .3px; margin-bottom: 10px; }
  p.sub { font-size: 14px; color: #8b94a7; margin-bottom: 30px; text-align: center; max-width: 420px; line-height: 1.5; }
  .bar {
    width: 220px; height: 3px; border-radius: 3px;
    background: #232733; overflow: hidden; position: relative;
  }
  .bar::after {
    content: ""; position: absolute; left: -40%; width: 40%; height: 100%;
    background: linear-gradient(90deg, transparent, #5b8cff, transparent);
    animation: slide 1.4s ease-in-out infinite;
  }
  @keyframes slide { to { left: 100%; } }
  .status { margin-top: 22px; font-size: 12px; color: #5b6473; font-family: ui-monospace, monospace; }
  .err { color: #ff7b72; }
</style>
</head>
<body>
  <div class="logo">/</div>
  <h1>Setting up your vvvvibe-coding environment</h1>
  <p class="sub">Sit back and relax — the chat is starting up.<br>
  First launch can take a moment while the AI backend initializes.</p>
  <div class="bar"></div>
  <div class="status" id="st">connecting…</div>
<script>
  // Poll the BRIDGE (same origin) for readiness. When Open WebUI is up, RELOAD —
  // the bridge's /chat then 302-redirects server-side to Open WebUI. This avoids
  // client-side cross-origin navigation, which CEF may silently swallow.
  const st = document.getElementById("st");
  let attempts = 0;
  async function poll() {
    attempts++;
    try {
      const r = await fetch("/api/chat/status", { cache: "no-store" });
      const j = await r.json();
      if (j.ready) {
        st.textContent = "ready — opening chat…";
        setTimeout(() => window.location.reload(), 400);
        return;
      }
      st.textContent = j.error ? ("problem: " + j.error) : (j.status || "waiting for the chat server…");
      if (j.error) { st.classList.add("err"); }
    } catch (e) {
      st.textContent = "waiting for the bridge… (" + attempts + ")";
    }
    setTimeout(poll, 1500);
  }
  poll();
</script>
</body>
</html>
""";
    }
}
