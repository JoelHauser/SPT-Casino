# Source art

Originals, at the size they were drawn. **Nothing in here ships** -- `pack.ps1` copies
`src/<Table>.Client/assets/*` and never looks at this folder.

Kept because the shipped tiles are a *treatment* of these, not a copy, and the treatment
is easy to get wrong from a 320px PNG that has already had it applied once.

| File | Shipped as | Treatment |
| --- | --- | --- |
| `tile-horseracing-source.png` | `src/Casino.Client/assets/tile-horseracing.png` | Luminance inverted, toned to 93%, resized 1254 -> 320 |

**Why the horse is inverted and the other four tiles are not.** It was drawn for a light
background -- black fill, white outline. The lobby tile is `0.10, 0.11, 0.12`, so the
fill merged into it and all that survived was a thin outline: on the tile row it read as
a faint sketch beside four solid cream glyphs. Inverting turns it into a cream shape with
dark detail, which is what the other four already are, and the 93% tone lands it on
`237, 234, 226` -- the cream the rest of the set uses.

To redo it, or to go back to the original:

```
python -c "from PIL import Image; import numpy as np; \
a=np.array(Image.open('art/tile-horseracing-source.png').convert('RGBA')).astype(np.int16); \
a[...,:3]=((255-a[...,:3])*0.93).astype(np.int16); \
Image.fromarray(np.clip(a,0,255).astype('uint8'),'RGBA').resize((320,320),Image.LANCZOS)\
.save('src/Casino.Client/assets/tile-horseracing.png',optimize=True)"
```

Drop the two `a[...,:3]` lines to ship it untreated.
