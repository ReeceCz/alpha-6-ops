# s4ops.com landing site

Static site served by Cloudflare static assets. The hero map is **simulated**. No real flight data is used: the
traffic, the callsigns (apart from S4) and the operators are generated in `public/traffic.mjs`.

## Develop

```
cd sites/s4ops.com
npx wrangler dev --port 8787            # serves public/ with _headers applied
node --test "tests/*.test.mjs"          # simulation and data tests (Node 22+)
```

No framework, no build step and no npm dependencies. Wrangler runs through `npx`. Every script, style, font and image is
served from this site, as the Content-Security-Policy in `public/_headers` requires (no inline scripts or `style=`).

## Map outlines

`public/data/great-lakes.json` is generated from Natural Earth 1:50m lakes (public domain) by
`python tools/build_lakes.py`. Commit the output, and don't hand-edit it. `public/data/provenance.json` records the source.

## Deploy (Reece only)

1. Cloudflare dashboard: the `s4ops.com` zone must be active.
2. `cd sites/s4ops.com && npx wrangler login && npx wrangler deploy`
3. In Workers & Pages → `s4ops-landing` → Settings → Domains & Routes, add the custom domains `s4ops.com` and `www.s4ops.com`.
4. Check that `https://s4ops.com/` loads and that its response includes the `Content-Security-Policy` header.

All account links go to `https://app.s4ops.com`. The download version and checksum live only on the portal's
`/Download` page.
