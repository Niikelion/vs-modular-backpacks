import assert from "node:assert/strict";
import { test } from "node:test";
import { matrixHtml, type MatrixEntry } from "./support/toolstrap-matrix-report.js";

const entry: MatrixEntry = {
  group: "Walking stick", code: "walkingstick:walkingstick", label: "Walking stick", file: "stick.png",
  accepted: true, available: true, inventoryAccepted: true, attachmentAccepted: true, moved: 1,
  reason: "Inserted.",
};

test("matrix distinguishes inserted items from rejected inventory and attachment filters", () => {
  const report = matrixHtml([
    entry,
    { ...entry, accepted: false, inventoryAccepted: false, moved: 0, reason: "Inventory filter rejects this item." },
    { ...entry, accepted: false, attachmentAccepted: false, moved: 0, reason: "Attachment point rejects this item." },
  ]);
  assert.equal(report.match(/class="accepted"/g)?.length, 1);
  assert.equal(report.match(/class="rejected"/g)?.length, 2);
  assert.match(report, /1\/3 passed; 2 failed/);
  assert.match(report, /PASS — inserted/);
  assert.match(report, /FAIL — cannot insert/);
  assert.match(report, /Inventory filter rejects this item\./);
  assert.match(report, /Attachment point rejects this item\./);
  assert.match(report, /figure\.rejected \{ border-color: #f36b6b; background: #451b1b;/);
});

test("missing items get a red failure card rather than disappearing", () => {
  const report = matrixHtml([{ ...entry, accepted: false, available: false, reason: "Item was not found." }]);
  assert.match(report, /class="rejected"/);
  assert.match(report, /FAIL — item missing/);
  assert.match(report, /0\/1 passed; 1 failed/);
});

test("successful filter checks are not enough when transfer fails", () => {
  const report = matrixHtml([{ ...entry, accepted: false, moved: 0, reason: "Slot transfer failed." }]);
  assert.match(report, /class="rejected"/);
  assert.doesNotMatch(report, /PASS — inserted/);
  assert.match(report, /Slot transfer failed\./);
});

test("matrix escapes item metadata and failure reasons", () => {
  const report = matrixHtml([{ ...entry, label: '<script>"axe"</script>', reason: "A & B < C" }]);
  assert.doesNotMatch(report, /<script>/);
  assert.match(report, /&lt;script&gt;&quot;axe&quot;&lt;\/script&gt;/);
  assert.match(report, /A &amp; B &lt; C/);
});
