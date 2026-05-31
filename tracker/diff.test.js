/* Node tests for the diff engine. Run: node diff.test.js
 * Builds the live build from the sample fixtures and asserts the diff matches
 * the hand-checked expectations for the sample Ball Lightning Sorcerer. */
const fs = require("fs");
const path = require("path");
const { diff, phraseMatch } = require("./diff.js");

const S = path.join(__dirname, "..", "samples");
const target = JSON.parse(fs.readFileSync(path.join(S, "sample_target.json"), "utf8"));
const build = JSON.parse(fs.readFileSync(path.join(S, "sample_build.json"), "utf8"));

let pass = 0, fail = 0;
function check(name, cond) {
  if (cond) { pass++; console.log("  ok  - " + name); }
  else { fail++; console.log("  FAIL- " + name); }
}
function cat(report, id) { return report.categories.find((c) => c.id === id); }
function grp(c, name) { return c.groups.find((g) => g.name === name); }
function item(g, frag) { return g.items.find((i) => i.label.toLowerCase().includes(frag.toLowerCase())); }

// --- matcher unit checks ---
console.log("phraseMatch:");
check('"Total Armor" matches live "Armor"', phraseMatch("Total Armor", "Armor"));
check('"Critical Strike Chance" does NOT match "Critical Strike Damage"',
  !phraseMatch("Critical Strike Chance", "Critical Strike Damage"));
check('"Ranks to Ball Lightning" matches itself', phraseMatch("Ranks to Ball Lightning", "Ranks to Ball Lightning"));

const r = diff(target, build);
console.log("\noverall: " + r.overall.matched + "/" + r.overall.total + " (" + r.overall.pct + "%)");

console.log("\ngear:");
const gear = cat(r, "gear");
check("helm fully matched (4/4)", grp(gear, "Helm").matched === 4);
check("gloves fully matched (4/4)", grp(gear, "Gloves").matched === 4);
check("boots Damage Reduction NOT matched", item(grp(gear, "Boots"), "Damage Reduction").done === false);
check("boots Movement Speed matched", item(grp(gear, "Boots"), "Movement Speed").done === true);
check("chest Maximum Life matched", item(grp(gear, "Chest"), "Maximum Life").done === true);
check("chest Damage Reduction NOT matched", item(grp(gear, "Chest"), "Damage Reduction from Close").done === false);
check("weapon group present but empty match (no weapon captured)", grp(gear, "Weapon").matched === 0);

console.log("\nuniques:");
const uq = cat(r, "uniques");
check("Tal Rasha's matched", item(uq.groups[0], "Tal Rasha").done === true);
check("Raiment of the Infinite matched", item(uq.groups[0], "Raiment").done === true);
check("Esu's Heirloom NOT matched", item(uq.groups[0], "Esu").done === false);
check("Shroud of False Death NOT matched", item(uq.groups[0], "Shroud").done === false);

console.log("\naspects:");
const asp = cat(r, "aspects");
check("Aspect of Concentration matched (from vision aspects)", item(asp.groups[0], "Concentration").done === true);
check("Storm Swell matched", item(asp.groups[0], "Storm Swell").done === true);
check("Recharging Aspect NOT matched", item(asp.groups[0], "Recharging").done === false);

console.log("\nskills:");
const sk = cat(r, "skills");
check("Ball Lightning 5/5 matched", item(grp(sk, "Active Skills"), "Ball Lightning").done === true);
check("Spark NOT matched", item(grp(sk, "Active Skills"), "Spark").done === false);
check("Vyr's Mastery key passive matched", item(grp(sk, "Key Passives"), "Vyr").done === true);

console.log("\nparagon:");
const pa = cat(r, "paragon");
check("Starter Board matched", item(grp(pa, "Boards"), "Starter").done === true);
check("Ceaseless Conduit NOT matched", item(grp(pa, "Boards"), "Ceaseless").done === false);
check("Tactician glyph at 12 fails target 15", item(grp(pa, "Glyphs"), "Tactician").done === false);
check("Destruction glyph at 21 satisfies target 15", item(grp(pa, "Glyphs"), "Destruction").done === true);
check("Elementalist glyph NOT socketed -> not matched", item(grp(pa, "Glyphs"), "Elementalist").done === false);

console.log("\n" + pass + " passed, " + fail + " failed");
process.exit(fail ? 1 : 0);
