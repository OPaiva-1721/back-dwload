import argparse
from pathlib import Path

from .config import COOKIES_FILE
from .sessao import exportar_cookies, yt_login


def main() -> None:
    p = argparse.ArgumentParser(prog="yt_cookies")
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("login", help="login manual (uma vez)")
    exp = sub.add_parser("export", help="exporta cookies.txt para o backend")
    exp.add_argument("--saida", type=Path, default=COOKIES_FILE)
    args = p.parse_args()
    if args.cmd == "login":
        yt_login()
    else:
        print(f"Cookies exportados para {exportar_cookies(args.saida)}")


main()
