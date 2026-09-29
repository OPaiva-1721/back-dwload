from pathlib import Path

RAIZ = Path(__file__).resolve().parents[2]
DATA_DIR = Path(__file__).resolve().parents[1] / "data"   # gitignored: guarda a sessão da conta
PERFIL_DIR = DATA_DIR / "yt_profile"
# O backend lê este arquivo (YtDlp:CookiesFile / volume do docker-compose)
COOKIES_FILE = RAIZ / "dwloads-back" / "cookies.txt"
