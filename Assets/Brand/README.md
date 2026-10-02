# Meerkat — identita e icone

Suricato in posizione di vedetta, costruito con facce triangolari. Riprende il linguaggio visivo di Platypus: petrolio, ambra e scritta sans-serif compatta. Il simbolo e disegnato per restare riconoscibile nella scheda Grasshopper a 24 px.

- `logo.svg` / `logo.png`: logo orizzontale trasparente per sfondi chiari.
- `logo-dark.svg` / `logo-dark.png`: variante per sfondi scuri.
- `mark.svg` / `mark.png`: simbolo trasparente; `mark-mono.*`: variante monocromatica.
- `icon-16.png` fino a `icon-512.png`: otto risoluzioni; il plugin usa 24 px.
- `meerkat.ico`: icona Windows multirisoluzione, da 16 a 256 px.
- `identity.svg` / `identity.png`: tavola con logo chiaro/scuro e tutte le icone.
- `../Icons/catalog.html`: catalogo consultabile offline, con ricerca, SVG e PNG dei 19 componenti.

Palette: petrolio `#12666B`, ambra `#E6AA4D`, scuro `#183A42`, chiaro `#F5F3ED`. Le famiglie dei componenti hanno colori distinti: lettura petrolio, parametri ambra, vista blu, controlli viola, salvataggio verde. Ogni icona ha un pittogramma dedicato, senza sigle sovrapposte.

SVG modificabili; scritta Arial Bold (Arial o un equivalente sans-serif deve essere disponibile quando si rasterizza). I PNG e l'ICO sono gia pronti. Le icone sono incorporate nell'assembly, percio non dipendono da percorsi esterni durante l'uso in Grasshopper.

Nel repository, rigenera con `npm install --prefix tools` e `node tools/generate-brand.cjs`, quindi `Build.ps1`. La normale compilazione usa gli asset gia generati e non richiede Node.js.
