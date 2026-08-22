const fs = require('fs');
const dir = 'C:/Users/Conas/.zcode/cli/rollout';

function lastText(f) {
  const fd = fs.openSync(f, 'r');
  const size = fs.fstatSync(fd).size;
  const len = Math.min(65536, size);
  const buf = Buffer.alloc(len);
  fs.readSync(fd, buf, 0, len, Math.max(0, size - len));
  fs.closeSync(fd);
  const s = buf.toString('utf8');
  const marker = '"text":"';
  let idx = s.lastIndexOf(marker);
  while (idx >= 0) {
    let j = idx + marker.length;
    let frag = '';
    while (j < s.length) {
      if (s[j] === '\\' && j + 1 < s.length) { frag += s[j] + s[j + 1]; j += 2; continue; }
      if (s[j] === '"') break;
      frag += s[j];
      j++;
    }
    let val = null;
    try { val = JSON.parse('"' + frag + '"').trim(); } catch (e) {}
    if (val) return val;
    idx = s.lastIndexOf(marker, idx - 1);
  }
  return null;
}

const files = fs.readdirSync(dir).filter(x => x.endsWith('.jsonl')).slice(-6);
for (const f of files) {
  const t = lastText(dir + '/' + f);
  const sid = f.slice(-13, -6);
  if (t == null) { console.log(sid, '| NO TEXT FOUND'); continue; }
  const stripped = t.replace(/[\s"'「」『』()（）\[\]【】…]+$/, '');
  const endsQ = /[？?]$/.test(stripped);
  console.log(sid, '| ends?', endsQ, '| ...' + t.slice(-30).replace(/\n/g, ' '));
}
