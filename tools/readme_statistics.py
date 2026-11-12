"""Calculate RC2 README statistics from the frozen ledger, or redraw the charts.

Usage: python tools/readme_statistics.py [--ledger PATH] --font FONT_FILE
Chart dependency: matplotlib. The default input is the published aggregate JSON.
Original code: copyright 柔軟的蝦. All rights reserved.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LEDGER_SHA256 = "13deed7067261849d96071dc9f215d0f6f25f9a5d9dfa626662debc3c609ebb3"

# These are observed before/after pairs, grouped for this project's statistics.
# A decrease in the old term and an increase in the new term must both occur.
PAIRS = [
    ("用語", "解析度", "解像度"), ("用語", "控制器", "手掣"),
    ("用語", "什麼", "甚麼"), ("用語", "菁英", "精英"),
    ("用語", "蒐集", "收集"), ("用語", "軟體", "軟件"),
    ("用語", "硬體", "硬件"),
    ("字形", "計畫", "計劃"), ("字形", "裡", "裏"),
    ("字形", "砲", "炮"),
    ("譯名", "拿索", "拿騷"), ("譯名", "金士頓", "京斯敦"),
    ("譯名", "伍德斯．羅傑斯", "活士．羅渣士"),
    ("譯名", "詹姆士．奇德", "占士．奇德"),
    ("譯名", "查爾斯．范恩", "查理斯．范恩"),
    ("譯名", "巴索羅穆．羅伯茲", "巴索羅繆．羅拔茨"),
    ("譯名", "卡洛琳．史考特", "嘉露蓮．史葛"),
    ("譯名", "朱利安．達卡斯", "祖利安．杜卡斯"),
    ("譯名", "亞伯．加旺奧", "阿貝爾．加爾旺"),
    ("譯名", "凡斯．崔佛斯", "萬斯．特拉弗斯"),
    ("譯名", "喬治", "佐治"),
    ("譯名", "巴貝多", "巴巴多斯"), ("譯名", "斯溫席", "史雲斯"),
    ("譯名", "卡地夫", "卡迪夫"), ("譯名", "西維爾", "塞維利亞"),
    ("譯名", "維吉尼亞", "弗吉尼亞"),
]


def calculate(ledger_path):
    raw = ledger_path.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == LEDGER_SHA256, "Unexpected ledger version"
    ledger = json.loads(raw)
    ids = [(x["kind"], x["key"]) for x in ledger]
    assert len(ids) == len(set(ids))
    counts = Counter(x["decision"] for x in ledger)
    assert counts == {"edit": 11800, "keep": 8980, "missing_target": 93}
    for row in ledger:
        assert row["applied_edit"] == (row["decision"] == "edit")
        if row["decision"] != "missing_target":
            assert (row["original_zh_hant"] != row["effective_target"]) == row["applied_edit"]
    by_kind = {
        kind: dict(Counter(x["decision"] for x in ledger if x["kind"] == kind))
        for kind in ("ui", "dialogue")
    }
    pair_counts, all_rows, categories = [], set(), {}
    occurrences = 0
    for category, old, new in PAIRS:
        matched, hits = set(), 0
        for row in ledger:
            if not row["applied_edit"]:
                continue
            before = row["original_zh_hant"].replace("\u200b", "")
            after = row["effective_target"].replace("\u200b", "")
            if old == "喬治" and "king george" not in row["english"].lower():
                continue
            n = min(max(0, before.count(old) - after.count(old)),
                    max(0, after.count(new) - before.count(new)))
            if n:
                matched.add((row["kind"], row["key"]))
                hits += n
        pair_counts.append(dict(category=category, original=old, revised=new,
                                segments=len(matched), occurrences=hits))
        categories.setdefault(category, set()).update(matched)
        all_rows.update(matched)
        occurrences += hits
    cases = json.loads((ROOT / "docs/semantic-example-classification.json").read_text("utf-8"))
    lookup = {(x["kind"], x["key"]): x for x in ledger}
    assert len(cases) == 60 and len({(x["kind"], x["key"]) for x in cases}) == 60
    for case in cases:
        assert lookup[case["kind"], case["key"]]["applied_edit"]
    semantic = Counter(c["classification"] for c in cases)
    return dict(
        version="2026.11.11-rc2", ledger_sha256=LEDGER_SHA256,
        unit="unique (kind, key) segment", total_records=len(ledger),
        paired_targets=counts["edit"] + counts["keep"],
        changed=counts["edit"], retained=counts["keep"],
        unpaired_proposals=counts["missing_target"], by_kind=by_kind,
        semantic_examples=dict(scope="README examples 1–60", total=len(cases),
                               categories=dict(semantic)),
        hk_conversion=dict(scope="26 explicit before/after term pairs",
                           unique_segments=len(all_rows), occurrences=occurrences,
                           categories={k: len(v) for k, v in categories.items()},
                           pairs=pair_counts),
    )


def draw(data, font_path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.font_manager import FontProperties
    from matplotlib import rcParams

    font = FontProperties(fname=str(font_path))
    rcParams.update({"font.family": font.get_name(), "axes.unicode_minus": False,
                     "savefig.facecolor": "#101f2c", "figure.facecolor": "#101f2c"})
    # Register the file so the same selected CJK font is used by every label.
    from matplotlib import font_manager
    font_manager.fontManager.addfont(str(font_path))
    bg, fg, muted = "#101f2c", "#f7f1e5", "#bbcbd3"
    gold, teal = "#e1b35d", "#61c3b6"

    def canvas(title, subtitle, height=6):
        fig = plt.figure(figsize=(14, height), dpi=140)
        fig.text(.055, .93, title, color=fg, fontsize=25, weight="bold")
        fig.text(.055, .865, subtitle, color=muted, fontsize=12)
        fig.text(.055, .035, "Black Flag Resynced  ·  RC2  ·  柔軟的蝦", color=muted, fontsize=10)
        return fig

    def save(fig, name):
        fig.savefig(ROOT / "assets" / name, dpi=140)
        plt.close(fig)

    fig = canvas("全庫處理：改動與保留", "20,780 個原有繁中目標句段  ·  另有 93 條單邊英文建議譯文")
    fig.text(.07, .70, f'{data["changed"]:,}', fontsize=36, color=gold, weight="bold")
    fig.text(.07, .64, f'已改動  ·  {data["changed"] / data["paired_targets"]:.1%}', fontsize=14, color=fg)
    fig.text(.48, .70, f'{data["retained"]:,}', fontsize=36, color=teal, weight="bold")
    fig.text(.48, .64, f'保留原譯  ·  {data["retained"] / data["paired_targets"]:.1%}', fontsize=14, color=fg)
    ax = fig.add_axes([.13, .22, .78, .29], facecolor=bg)
    for y, kind, label in [(1, "ui", "介面"), (0, "dialogue", "對白")]:
        c = data["by_kind"][kind]
        ax.barh(y, c["edit"], height=.47, color=gold)
        ax.barh(y, c["keep"], left=c["edit"], height=.47, color=teal)
        for left, val in [(0, c["edit"]), (c["edit"], c["keep"])]:
            ax.text(left + val / 2, y, f'{val:,}', ha="center", va="center", color=bg,
                    fontsize=12 if val > 500 else 10, weight="bold")
        ax.text(-220, y, label, ha="right", va="center", color=fg, fontsize=14)
    ax.set_xlim(0, 12000); ax.set_ylim(-.6, 1.6); ax.axis("off")
    fig.text(.13, .16, "金色：改動   ·   綠色：保留原譯   ｜   每個資源句段鍵計一次", color=muted, fontsize=12)
    save(fig, "statistics-changes.png")

    s = data["semantic_examples"]["categories"]
    fig = canvas("原譯語義：60 個例子的逐項分類", "統計範圍：README 已刊的 60 個英中對照例子", height=5.5)
    ax = fig.add_axes([.06, .19, .38, .59], facecolor=bg)
    ax.pie([s["語義錯漏"], s["補足及精確化"]], colors=[gold, teal], startangle=90,
           counterclock=False, wedgeprops=dict(width=.25, edgecolor=bg, linewidth=3))
    ax.text(0, .08, "60", ha="center", va="center", fontsize=40, color=fg, weight="bold")
    ax.text(0, -.23, "個已刊例子", ha="center", va="center", fontsize=13, color=muted)
    fig.text(.48, .65, f'{s["語義錯漏"]} 段  語義錯漏', color=gold, fontsize=25, weight="bold")
    fig.text(.48, .56, "詞義、慣用語、邏輯、時序及主要資訊偏差", color=fg, fontsize=13)
    fig.text(.48, .40, f'{s["補足及精確化"]} 段  補足及精確化', color=teal, fontsize=25, weight="bold")
    fig.text(.48, .31, "補充細節、收窄歧義及調整術語精確度", color=fg, fontsize=13)
    fig.text(.48, .17, "逐項分類、句段鍵及理由見統計說明。", color=muted, fontsize=12)
    save(fig, "statistics-semantics.png")

    h = data["hk_conversion"]
    fig = canvas("台譯港：用語、字形及譯名", "全庫比對 26 組明列詞對  ·  同一句段跨詞對去重", height=7.5)
    fig.text(.07, .73, f'{h["unique_segments"]:,} 段', fontsize=38, color=gold, weight="bold")
    fig.text(.07, .67, f'錄得 {h["occurrences"]:,} 次詞對轉換', fontsize=15, color=fg)
    for x, (cat, n) in zip([.49, .65, .81], h["categories"].items()):
        fig.text(x, .75, f'{n:,}', fontsize=27, color=teal, weight="bold")
        fig.text(x, .69, cat + "句段", fontsize=13, color=fg)
    top = sorted(h["pairs"], key=lambda p: p["segments"], reverse=True)[:8]
    ax = fig.add_axes([.29, .16, .61, .43], facecolor=bg)
    ax.barh(range(len(top)), [p["segments"] for p in top], color=teal, height=.6)
    for i, p in enumerate(top):
        ax.text(-12, i, f'{p["original"]} → {p["revised"]}', color=fg, fontsize=12, ha="right", va="center")
        ax.text(p["segments"] + 10, i, str(p["segments"]), color=gold, fontsize=12, va="center", weight="bold")
    ax.set_xlim(0, max(p["segments"] for p in top) * 1.12)
    ax.set_ylim(len(top) - .4, -.8); ax.axis("off")
    fig.text(.29, .105, "常見詞對：涉及句段數   ｜   各類句段可重疊", color=muted, fontsize=12)
    save(fig, "statistics-hk.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ledger", type=Path)
    parser.add_argument("--font", type=Path, required=True)
    args = parser.parse_args()
    target = ROOT / "docs/statistics.json"
    if args.ledger:
        data = calculate(args.ledger)
        target.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", "utf-8")
    else:
        data = json.loads(target.read_text("utf-8"))
    draw(data, args.font)
    print(json.dumps({"changed": data["changed"], "retained": data["retained"],
                      "semantic_examples": data["semantic_examples"],
                      "hk_segments": data["hk_conversion"]["unique_segments"],
                      "hk_occurrences": data["hk_conversion"]["occurrences"],
                      "hk_categories": data["hk_conversion"]["categories"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
