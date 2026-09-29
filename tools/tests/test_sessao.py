import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from yt_cookies.sessao import cookies_para_netscape  # noqa: E402


def _c(name, domain=".youtube.com", expires=1700000000.5, secure=True):
    return {"name": name, "value": "v", "domain": domain, "path": "/",
            "secure": secure, "expires": expires}


def test_formato_netscape():
    saida = cookies_para_netscape([_c("SAPISID"), _c("x", domain="accounts.google.com",
                                                     expires=-1, secure=False)])
    linhas = saida.splitlines()
    assert linhas[0] == "# Netscape HTTP Cookie File"
    assert linhas[1].split("\t") == [".youtube.com", "TRUE", "/", "TRUE", "1700000000", "SAPISID", "v"]
    assert linhas[2].split("\t") == ["accounts.google.com", "FALSE", "/", "FALSE", "0", "x", "v"]


def test_sem_login_levanta_erro():
    with pytest.raises(RuntimeError, match="não está logado"):
        cookies_para_netscape([_c("VISITOR_INFO1_LIVE")])
