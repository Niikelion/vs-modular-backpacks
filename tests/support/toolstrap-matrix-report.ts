export interface MatrixItem {
  group: string;
  code: string;
  label: string;
  preset?: string;
}

export interface SlotAcceptance {
  accepted: boolean;
  available: boolean;
  inventoryAccepted?: boolean;
  attachmentAccepted?: boolean;
  moved?: number;
  reason: string;
}

export type MatrixEntry = MatrixItem & SlotAcceptance & { file: string };

export function matrixHtml(items: MatrixEntry[]): string {
  const rejected = items.filter(item => !item.accepted).length;
  const cards = items.map((item) => `
    <figure class="${item.accepted ? "accepted" : "rejected"}">
      <img src="${escapeHtml(item.file)}" alt="${escapeHtml(item.label)}">
      <figcaption>
        <strong>${escapeHtml(item.label)}</strong><code>${escapeHtml(item.code)}</code>
        <span class="status">${item.accepted ? "PASS — inserted" : item.available ? "FAIL — cannot insert" : "FAIL — item missing"}</span>
        <span class="reason">${escapeHtml(item.reason)}</span>
      </figcaption>
    </figure>`).join("");

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Toolstrap compatibility and offset matrix</title>
  <style>
    :root { color-scheme: dark; font: 14px system-ui, sans-serif; background: #171717; color: #eee; }
    body { margin: 24px; }
    h1 { margin: 0 0 6px; }
    p { margin: 0 0 20px; color: #aaa; }
    main { display: grid; grid-template-columns: repeat(auto-fit, minmax(630px, 1fr)); gap: 16px; }
    figure { margin: 0; overflow: hidden; border: 2px solid #3b3b3b; border-radius: 8px; background: #222; }
    figure.rejected { border-color: #f36b6b; background: #451b1b; }
    .rejected .status { color: #ffaaaa; font-weight: bold; }
    .accepted .status { color: #a4d8a4; }
    img { display: block; width: 100%; aspect-ratio: 1; object-fit: cover; }
    figcaption { display: grid; gap: 6px; padding: 10px 12px; }
    code { color: #aaa; }
  </style>
</head>
<body>
  <h1>Toolstrap compatibility and offset matrix</h1>
  <p>${items.length - rejected}/${items.length} passed; ${rejected} failed. Red cards indicate rejected or missing items.
  Every pass requires attachment acceptance and an actual inventory transfer. Failed items are not forced into the slot.</p>
  <main>${cards}
  </main>
</body>
</html>
`;
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>\"]/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[char]!);
}
