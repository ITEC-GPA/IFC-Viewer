// Vector-first identity, matching the geometric petrol/amber Platypus family.
// Run: npm install --prefix tools ; node tools/generate-brand.cjs
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '..');
const brand = path.join(root, 'Assets', 'Brand');
const icons = path.join(root, 'Assets', 'Icons');
for (const dir of [brand, icons, path.join(icons, 'svg'), path.join(icons, 'png')]) fs.mkdirSync(dir, {recursive:true});
const svg = (w,h,body) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">${body}</svg>`;
const mark = (dark=false, mono=false) => {
  const colors = mono ? Array(7).fill(dark?'#F5F3ED':'#183A42') : dark
    ? ['#70C4C0','#8BD3CD','#4DA7A9','#60B6B5','#E6AA4D','#F0BC66','#D49739']
    : ['#12666B','#31888A','#0E565D','#217A7D','#E6AA4D','#F0BC66','#D49739'];
  const faces = [
    // Tail, rear leg and feet, followed by the upright torso.
    ['52,88 39,101 18,108 35,108 55,99',2], ['18,108 9,106 19,114 35,108',0],
    ['52,95 53,112 43,119 66,119 67,110 65,98',2], ['68,97 71,111 83,117 83,120 61,120 63,108',0],
    ['47,92 42,79 51,67 59,84',1], ['51,67 59,52 68,60 59,84',0],
    ['59,52 68,43 74,53 68,60',1], ['74,53 80,72 68,60',0],
    ['80,72 76,92 59,84',2], ['80,72 68,60 59,84',3],
    ['76,92 65,108 59,84',0], ['65,108 47,92 59,84',3],
    // Folded forelegs are compact enough to survive the 24 px icon.
    ['67,62 79,72 91,71 86,77 73,78 64,70',4],
    // Long neck and head, with an angular muzzle and small ear.
    ['59,52 57,35 69,39 68,43',6], ['57,35 54,23 69,25 69,39',4],
    ['54,23 60,12 72,10 69,25',5], ['72,10 84,16 88,27 69,25',4],
    ['88,27 101,32 105,37 91,42 77,40 69,25',5],
    ['69,25 77,40 69,39 57,35',6], ['69,39 77,40 74,53 68,43',4],
  ];
  return `<g stroke="${mono?'none':'#C6E5DE'}" stroke-opacity=".38" stroke-width=".65" stroke-linejoin="round">${faces.map(([p,c])=>`<polygon points="${p}" fill="${colors[c]}"/>`).join('')}</g>`+
    `<path d="M57 20L52 17L53 11L60 12L64 19Z" fill="${colors[2]}"/>`+
    `<path d="M77 23L87 27L85 32L75 29Z" fill="${mono?(dark?'#183A42':'#F5F3ED'):'#183A42'}"/>`+
    `<circle cx="82" cy="27" r="1.55" fill="${mono?(dark?'#F5F3ED':'#183A42'):'#FFFFFF'}"/>`+
    `<path d="M101 32L106 35L104 38L99 36Z" fill="${colors[2]}"/>`;
};
const wordmark = (dark=false) => `<text x="201" y="106" font-family="Arial, sans-serif" font-size="72" font-weight="700" letter-spacing="-2.8" fill="${dark?'#F5F3ED':'#183A42'}">Meerkat</text><text x="206" y="144" font-family="Arial, sans-serif" font-size="14" letter-spacing="2.2" fill="${dark?'#B2CED0':'#58747A'}">IFC &amp; BIM · GRASSHOPPER</text>`;
const logoBody = (dark=false) => `<g transform="translate(16 2) scale(1.4)">${mark(dark)}</g>${wordmark(dark)}`;
const glyphs = [
 ['ReadIfcComponent','Read IFC','Lettura','#267E87','<path d="M5 3h9l4 4v5 M14 3v5h5 M5 3v17h7 M14 16h7m-3-3 3 3-3 3"/>'],
 ['ModelInfoComponent','IFC Model Info','Lettura','#267E87','<path d="M4 20V7l7-4v17M11 9h8v11M3 20h18M7 7v2m0 3v2m0 3v1M15 12v1m0 3v1"/>'],
 ['InspectIfcComponent','IFC Elements','Lettura','#267E87','<path d="M3 8l7-4 7 4-7 4zM3 8v8l7 4 4-2M10 12v8"/><circle cx="17" cy="15" r="3"/><path d="M19 17l3 4"/>'],
 ['ReadBakedComponent','IFC Read Baked','Lettura','#267E87','<path d="M3 10l6-3 6 3v8l-6 3-6-3zM3 10l6 3 6-3M9 13v8M21 12a7 7 0 0 0-9-8M12 2v4h4"/>'],
 ['PropertyKeysComponent','IFC Property Keys','Parametri','#B36A29','<circle cx="8" cy="8" r="4"/><path d="M11 11l9 9m-6-6 3-3m0 6 3-3"/>'],
 ['PropertyValueComponent','IFC Property Value','Parametri','#B36A29','<path d="M5 3h14v18H5zM8 7h8M8 11h3M8 15h3M8 18h3M14 12l3 3-3 3"/>'],
 ['FilterPropertyComponent','IFC Filter Property','Parametri','#B36A29','<path d="M3 4h18l-7 9v6l-4 2v-8z"/>'],
 ['SelectIdsComponent','IFC Select IDs','Parametri','#B36A29','<path d="M3 8V3h5m8 0h5v5M3 16v5h5m8 0h5v-5M10 7l-2 10m7-10-2 10M6 10h12M5 14h12"/>'],
 ['GroupByComponent','IFC Group By','Parametri','#B36A29','<rect x="8" y="3" width="8" height="5" rx="1"/><rect x="2" y="16" width="8" height="5" rx="1"/><rect x="14" y="16" width="8" height="5" rx="1"/><path d="M12 8v4M6 16v-4h12v4"/>'],
 ['SortElementsComponent','IFC Sort','Parametri','#B36A29','<path d="M4 5h3M4 11h7M4 17h11M19 3v17m-3-3 3 3 3-3"/>'],
 ['ColorByComponent','IFC Color By','Vista','#356AAF','<path d="M12 3a9 9 0 1 0 2 18c3-1-1-4 1-6s7 1 6-5c-1-4-5-7-9-7z"/><circle cx="8" cy="7" r="1"/><circle cx="16" cy="7" r="1"/><circle cx="6" cy="13" r="1"/><circle cx="10" cy="17" r="1"/>'],
 ['GeometryMetricsComponent','IFC Geometry Metrics','Vista','#356AAF','<path d="M3 8l9-5 9 5v9l-9 5-9-5zM3 8l9 5 9-5M12 13v9M6 9v3m3-1v3m6-2v3m3-4v3"/>'],
 ['OriginComponent','IFC Local Origin','Vista','#356AAF','<circle cx="12" cy="12" r="6"/><path d="M12 2v20M2 12h20M16 4l-4-2-4 2M20 8l2 4-2 4"/>'],
 ['ValidateElementsComponent','IFC Check Data','Controlli','#6757AE','<path d="M5 4h14v17H5zM9 4V2h6v2M8 12l3 3 6-7"/>'],
 ['CompareRevisionsComponent','IFC Compare Revisions','Controlli','#6757AE','<path d="M3 3h7v17H3zM14 3h7v17h-7zM5 8h3m8 0h3m-1.5-1.5v3M5 15h3M16 15h3"/>'],
 ['BakeIfcComponent','Bake IFC','Salvataggio','#39805A','<path d="M3 13l9 4 9-4M3 17l9 4 9-4M12 2v11m-4-4 4 4 4-4M3 9l4-2m10 0 4 2"/>'],
 ['SaveIfcComponent','Save IFC Archive','Salvataggio','#39805A','<path d="M3 7h18v14H3zM2 3h20v4H2zM9 10h6v3H9z"/>'],
 ['ExportCsvComponent','IFC Export CSV','Salvataggio','#39805A','<path d="M3 3h13v18H3zM3 8h13M3 13h8M3 17h8M8 3v18M13 15h9m-3-3 3 3-3 3"/>'],
 ['Export3dmComponent','IFC Export 3DM','Salvataggio','#39805A','<path d="M2 7l7-4 7 4-7 4zM2 7v10l7 4 5-3M9 11v10M14 13h8m-3-3 3 3-3 3"/>'],
];
const iconSvg = (color,glyph) => svg(24,24,`<rect x=".5" y=".5" width="23" height="23" rx="4" fill="#F5F3ED" stroke="${color}" stroke-opacity=".28"/><g transform="translate(1 1) scale(.92)" fill="none" stroke="${color}" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">${glyph}</g>`);
async function render(name,content) {
 fs.writeFileSync(path.join(brand,name+'.svg'),content);
 await sharp(Buffer.from(content), {density:144}).resize(Number(content.match(/width="(\d+)"/)[1])).png().toFile(path.join(brand,name+'.png'));
}
(async()=>{
 const icon = svg(128,128,mark());
 await render('mark',icon); await render('mark-mono',svg(128,128,mark(false,true)));
 await render('logo',svg(730,190,logoBody())); await render('logo-dark',svg(730,190,logoBody(true)));
 const entries=[];
 for(const size of [16,24,32,48,64,128,256,512]) {
   const png = await sharp(Buffer.from(icon),{density:576}).resize(size,size).png().toBuffer();
   fs.writeFileSync(path.join(brand,`icon-${size}.png`),png); if(size<=256)entries.push({size,png});
 }
 const header=Buffer.alloc(6+16*entries.length); header.writeUInt16LE(1,2);header.writeUInt16LE(entries.length,4);let offset=header.length;
 entries.forEach(({size,png},i)=>{const p=6+16*i;header[p]=header[p+1]=size===256?0:size;header.writeUInt16LE(1,p+4);header.writeUInt16LE(32,p+6);header.writeUInt32LE(png.length,p+8);header.writeUInt32LE(offset,p+12);offset+=png.length;});
 fs.writeFileSync(path.join(brand,'meerkat.ico'),Buffer.concat([header,...entries.map(e=>e.png)]));
 for(const [key,name,family,color,glyph] of glyphs) {
   const content=iconSvg(color,glyph);fs.writeFileSync(path.join(icons,'svg',key+'.svg'),content);
   await sharp(Buffer.from(content),{density:288}).resize(24,24).png().toFile(path.join(icons,'png',key+'.png'));
 }
 const tiles=glyphs.map(([key,name,family,color,glyph],i)=>{const x=48+(i%5)*263,y=738+Math.floor(i/5)*100;return `<g transform="translate(${x} ${y})"><rect width="247" height="84" rx="9" fill="white"/><g transform="translate(14 12) scale(1.9)">${iconSvg(color,glyph).replace(/^.*?<svg[^>]*>/,'').replace(/<\/svg>$/,'')}</g><text x="71" y="28" font-family="Arial" font-size="12" fill="#183A42">${name}</text><text x="71" y="49" font-family="Arial" font-size="11" fill="#58747A">${family}</text></g>`;}).join('');
 await render('identity',svg(1400,1180,`<rect width="1400" height="1180" fill="#F5F3ED"/><text x="58" y="58" font-family="Arial" font-size="14" fill="#58747A" letter-spacing="3">MEERKAT / IFC &amp; BIM TOOLS</text><g transform="translate(140 86) scale(1.35)">${logoBody()}</g><rect x="48" y="370" width="1304" height="264" rx="18" fill="#183A42"/><g transform="translate(140 378) scale(1.3)">${logoBody(true)}</g><text x="58" y="693" font-family="Arial" font-size="14" fill="#58747A" letter-spacing="2">19 COMPONENTI / ICONE INCORPORATE A 24 PX</text>${tiles}`));
 const cards=glyphs.map(([key,name,family,color])=>`<article data-name="${name.toLowerCase()}"><div class="previews"><img src="png/${key}.png" width="24" height="24"><span class="dark"><img src="png/${key}.png" width="24" height="24"></span><img src="svg/${key}.svg" width="48" height="48"></div><h2>${name}</h2><p style="color:${color}">${family}</p><a href="svg/${key}.svg">SVG</a> · <a href="png/${key}.png">PNG 24 px</a></article>`).join('');
 fs.writeFileSync(path.join(icons,'catalog.html'),`<!doctype html><html lang="it"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Meerkat · Catalogo icone</title><style>body{font:15px Arial;background:#f5f3ed;color:#183a42;margin:40px auto;max-width:1200px;padding:0 24px}header{display:flex;gap:24px;align-items:center}h1{font-size:38px;margin:0}input{padding:14px;width:min(460px,90%);margin:24px 0;border:1px solid #91a8a6;border-radius:8px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(235px,1fr));gap:16px}article{padding:22px;border-radius:12px;background:white}h2{font-size:17px}a{color:#12666b}.previews{display:flex;align-items:center;gap:22px}.dark{background:#183a42;padding:12px;border-radius:8px}footer{margin:30px 0;color:#58747a}</style><header><img src="../Brand/icon-128.png" width="100" height="100" alt="Suricato Meerkat"><div><h1>Meerkat</h1><p>19 componenti · SVG modificabili e PNG Grasshopper</p></div></header><label for="search">Trova un componente</label><br><input id="search" type="search" placeholder="Filtra per nome"><main>${cards}</main><footer>Anteprime a 24 px su sfondo chiaro/scuro e sorgente vettoriale a 48 px.</footer><script>document.querySelector('input').addEventListener('input',e=>document.querySelectorAll('article').forEach(c=>c.hidden=!c.dataset.name.includes(e.target.value.toLowerCase())));</script></html>`);
 fs.writeFileSync(path.join(icons,'manifest.json'),JSON.stringify(glyphs.map(([key,name,family,color])=>({key,name,family,color,png:`png/${key}.png`,svg:`svg/${key}.svg`})),null,2));
 console.log('Meerkat: logo, SVG/PNG, ICO multirisoluzione, tavola e 19 icone generati.');
})().catch(e=>{console.error(e);process.exitCode=1;});
