import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";

const testsRoot = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(testsRoot, "artifacts", "toolstrap-matrix");
const host = process.env.IB_MATRIX_SERVE_HOST ?? "0.0.0.0";
const port = Number(process.env.IB_MATRIX_SERVE_PORT ?? 4173);
const contentTypes = new Map([
  [".html", "text/html; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".png", "image/png"],
]);

const server = createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url ?? "/", "http://localhost").pathname);
    const relativePath = pathname === "/" ? "index.html" : pathname.slice(1);
    const filePath = path.resolve(root, relativePath);
    if (filePath !== root && !filePath.startsWith(`${root}${path.sep}`)) {
      response.writeHead(403).end("Forbidden");
      return;
    }

    const info = await stat(filePath);
    if (!info.isFile()) throw new Error("Not a file");
    response.writeHead(200, { "Content-Type": contentTypes.get(path.extname(filePath)) ?? "application/octet-stream" });
    createReadStream(filePath).pipe(response);
  } catch {
    response.writeHead(404).end("Not found");
  }
});

server.listen(port, host, () => {
  console.log(`Toolstrap matrix server listening on http://${host}:${port}/`);
});
