/* Terminal report: print the diff of a TARGET vs a LIVE build, reusing diff.js.
 * Usage: node report.js <target.json> <build.json> */
const fs = require("fs");
const { diff } = require("./diff.js");

const [, , targetPath, livePath] = process.argv;
if (!targetPath || !livePath) {
  console.error("usage: node report.js <target.json> <build.json>");
  process.exit(2);
}
const target = JSON.parse(fs.readFileSync(targetPath, "utf8"));
const live = JSON.parse(fs.readFileSync(livePath, "utf8"));
const r = diff(target, live);

console.log("\n" + (r.target.name || "Build") + (r.target.class ? "  [" + r.target.class + "]" : ""));
console.log(r.overall.matched + " / " + r.overall.total + " requirements met  (" + r.overall.pct + "%)\n");
r.categories.forEach(function (c) {
  console.log(c.name + "  —  " + c.matched + "/" + c.total + " (" + c.pct + "%)");
  c.groups.forEach(function (g) {
    console.log("  " + g.name + "  (" + g.matched + "/" + g.total + ")");
    g.items.forEach(function (i) {
      console.log("    " + (i.done ? "[x]" : "[ ]") + " " + i.label + (i.done && i.source ? "  (" + i.source + ")" : ""));
    });
  });
  console.log("");
});
