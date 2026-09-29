# yt_cookies

Gera o `dwloads-back/cookies.txt` (formato Netscape) que o backend passa ao yt-dlp (`YtDlp:CookiesFile`),
a partir de um perfil Chrome logado numa conta Google **secundária**. Quem descriptografa os cookies é o
próprio Chrome (via Playwright), o que contorna a app-bound encryption do Chrome 127+.

```
pip install playwright        # e, se não houver Chrome: playwright install chromium
python -m yt_cookies login    # uma vez: faça login no YouTube e FECHE o navegador
python -m yt_cookies export   # (re)gera dwloads-back/cookies.txt
```

Notas:
- Rode a partir de `tools/`. Nenhum outro Chrome pode estar aberto com o mesmo perfil.
- Não navegue no perfil do bot: cada uso rotaciona os cookies e invalida o arquivo exportado. Reexporte quando o backend responder `YtDlp.AuthRequired`.
- `tools/data/` (perfil) e `cookies.txt` são ignorados pelo git; ambos dão acesso à conta.
- O backend precisa de `deno` (ou `node`) e `ffmpeg` no PATH e de yt-dlp atualizado.
- Testes: `pip install pytest && python -m pytest tools/tests`.
