import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

from .config import COOKIES_FILE, PERFIL_DIR

_COOKIES_DE_LOGIN = ("SAPISID", "__Secure-3PSID")


def achar_chrome() -> str:
    """Localiza o Google Chrome (registro/caminhos padrão no Windows, PATH no Linux/macOS)."""
    candidatos: list[str] = []
    if sys.platform == "win32":
        import winreg
        for raiz in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
            try:
                with winreg.OpenKey(
                    raiz, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
                ) as chave:
                    candidatos.append(winreg.QueryValue(chave, None))
            except OSError:
                pass
        for var in ("PROGRAMFILES", "PROGRAMFILES(X86)", "LOCALAPPDATA"):
            base = os.environ.get(var)
            if base:
                candidatos.append(str(Path(base) / "Google/Chrome/Application/chrome.exe"))
    elif sys.platform == "darwin":
        candidatos.append("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome")
    else:
        for nome in ("google-chrome", "google-chrome-stable", "chrome"):
            achado = shutil.which(nome)
            if achado:
                candidatos.append(achado)
    for c in candidatos:
        if c and Path(c).exists():
            return c
    raise FileNotFoundError("Google Chrome não encontrado. Instale-o para fazer o login.")


def yt_login() -> None:
    """Abre o Chrome normal (sem automação, senão o Google recusa o login) uma única vez."""
    chrome = achar_chrome()
    PERFIL_DIR.mkdir(parents=True, exist_ok=True)
    print("Faça login no YouTube com a conta secundária e FECHE o navegador.")
    inicio = time.monotonic()
    subprocess.Popen([
        chrome, f"--user-data-dir={PERFIL_DIR}", "--no-first-run",
        "--no-default-browser-check", "https://www.youtube.com",
    ]).wait()
    if time.monotonic() - inicio < 3:
        print("⚠️ Fechou rápido demais: já havia um Chrome aberto com esse perfil. "
              "Feche tudo e rode de novo.")


def cookies_para_netscape(cookies: list[dict]) -> str:
    if not any(c["name"] in _COOKIES_DE_LOGIN for c in cookies):
        raise RuntimeError("Perfil não está logado no YouTube — rode o comando 'login'.")
    linhas = ["# Netscape HTTP Cookie File"]
    for c in cookies:
        dominio = c["domain"]
        linhas.append("\t".join([
            dominio,
            "TRUE" if dominio.startswith(".") else "FALSE",
            c["path"],
            "TRUE" if c["secure"] else "FALSE",
            str(int(c["expires"])) if c["expires"] > 0 else "0",  # -1 = cookie de sessão
            c["name"],
            c["value"],
        ]))
    return "\n".join(linhas) + "\n"


def _ler_cookies() -> list[dict]:
    from playwright.sync_api import sync_playwright

    urls = ["https://www.youtube.com", "https://accounts.google.com"]
    args = ["--disable-blink-features=AutomationControlled"]
    with sync_playwright() as pw:
        try:
            ctx = pw.chromium.launch_persistent_context(
                str(PERFIL_DIR), channel="chrome", headless=True, args=args,
                ignore_default_args=["--enable-automation"],
            )
        except Exception:  # Chrome instalado ausente: cai no Chromium do Playwright
            ctx = pw.chromium.launch_persistent_context(
                str(PERFIL_DIR), headless=True, args=args,
                ignore_default_args=["--enable-automation"],
            )
        try:
            return ctx.cookies(urls)
        finally:
            ctx.close()


def exportar_cookies(destino: Path = COOKIES_FILE) -> Path:
    """Perfil -> cookies.txt. Quem descriptografa é o próprio Chrome (contorna app-bound encryption)."""
    conteudo = cookies_para_netscape(_ler_cookies())
    destino.parent.mkdir(parents=True, exist_ok=True)
    destino.write_text(conteudo, encoding="utf-8")
    return destino
