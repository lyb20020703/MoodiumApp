// Renders a design component (.dc.html) to a static HTML page, so headless Chrome can rasterize it.
// Usage: node render_component.js <Component.dc.html> '<props json>' <out.html>
// Supports the subset of the format the guide cards use: {{holes}}, <sc-if>, <sc-for>, <dc-import>.
const fs = require('fs');
const path = require('path');

const [, , file, propsJson, outFile] = process.argv;
const dir = path.dirname(file);
const cache = {};
const helmets = new Set();

function load(name) {
  if (cache[name]) return cache[name];
  const src = fs.readFileSync(path.join(dir, name + '.dc.html'), 'utf8');
  const tpl = src.slice(src.indexOf('<x-dc>') + 6, src.indexOf('</x-dc>')).replace(/<helmet>[\s\S]*?<\/helmet>/, '');
  const helmet = (src.match(/<helmet>([\s\S]*?)<\/helmet>/) || [, ''])[1];
  const code = src.slice(src.indexOf('class Component'), src.lastIndexOf('</script>'));
  class DCLogic { constructor(p) { this.props = p; this.state = {}; } setState() {} }
  const C = new Function('DCLogic', code + '; return Component;')(DCLogic);
  return (cache[name] = { tpl, helmet, C });
}

const get = (scope, p) => p.trim().split('.').reduce((o, k) => (o == null ? undefined : o[k]), scope);
const camel = (s) => s.replace(/-(\w)/g, (m, c) => c.toUpperCase());

function render(t, scope) {
  t = t.replace(/<sc-for list="\{\{([^}]+)\}\}" as="(\w+)"[^>]*>([\s\S]*?)<\/sc-for>/g, (m, list, as, body) =>
    (get(scope, list) || []).map((item, i) => render(body, Object.assign({}, scope, { [as]: item, $index: i }))).join(''));
  let prev;
  do {
    prev = t;
    t = t.replace(/<sc-if value="\{\{([^}]+)\}\}"[^>]*>((?:(?!<sc-if)[\s\S])*?)<\/sc-if>/, (m, c, body) => (get(scope, c) ? body : ''));
  } while (t !== prev);
  t = t.replace(/<dc-import ([^>]*)><\/dc-import>/g, (m, attrs) => {
    const props = {};
    let name;
    attrs.replace(/([\w-]+)="([^"]*)"/g, (mm, k, v) => {
      const val = /^\{\{[^}]+\}\}$/.test(v) ? get(scope, v.slice(2, -2)) : v.replace(/\{\{([^}]+)\}\}/g, (q, p) => get(scope, p));
      if (k === 'name') name = val; else props[camel(k)] = val;
    });
    return renderComponent(name, props);
  });
  t = t.replace(/(on[A-Z]\w*)="\{\{[^}]+\}\}"/g, '');
  return t.replace(/\{\{([^}]+)\}\}/g, (m, p) => { const v = get(scope, p); return v == null ? '' : String(v); });
}

function renderComponent(name, props) {
  const c = load(name);
  helmets.add(c.helmet);
  return render(c.tpl, new c.C(props).renderVals());
}

const body = renderComponent(path.basename(file, '.dc.html'), JSON.parse(propsJson));
fs.writeFileSync(outFile, '<!doctype html><html><head><meta charset="utf-8">' + [...helmets].join('') +
  '<style>html,body{margin:0;background:transparent}</style></head><body>' + body + '</body></html>');
