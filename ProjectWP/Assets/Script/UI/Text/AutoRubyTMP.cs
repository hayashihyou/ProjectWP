// =====================================================================
//  AutoRubyTMP.cs  --  TextMeshPro用 自動ルビ(ふりがな)システム【単一ファイル完結】
// =====================================================================
//
//  ■ 導入
//   1. このファイルを Assets 以下の「Editorフォルダ以外」に置く（ファイル名は変更しない）。
//   2. TMPテキストを右クリック → 「ルビ機能を追加 (AutoRuby)」
//      （または Add Component → AutoRuby → Auto Ruby TMP）
//   3. 本文を入力すると、漢字のルビが自動で生成・表示される。
//   4. 編集は Inspector、または メニュー Tools > AutoRuby > Ruby Text Editor。
//   5. 動作確認: メニュー Tools > AutoRuby > Create Sample Objects
//
//  ■ 必須: 日本語(漢字)を含むTMPフォントアセット（Dynamicアトラス推奨）を本文に設定すること。
//
//  ■ 読み辞書（解析エンジン）
//   - 本ファイル内蔵の小さな辞書 + 「ユーザー辞書(TextAsset)」だけでも動く。
//   - 精度を上げる場合: メニュー Tools > AutoRuby > Import MeCab-IPAdic CSV Folder...
//     で mecab-ipadic(UTF-8版推奨)や mecab-ipadic-NEologd の *.csv があるフォルダを指定。
//     取り込み結果は <Project>/Library/AutoRubyDict.tsv に保存（Assets外・ビルドに含まれない）。
//   - 解析は Editor 専用。ビルドには辞書も解析コードも入らない（#if UNITY_EDITOR）。
//   - ユーザー辞書書式: 1行1語  「表記<TAB>読み」(カンマ/縦線区切りも可、#で始まる行はコメント)
//
//  ■ 「読みの追加」ボタン（辞書への登録を楽にする機能）
//   Inspector「① 本文」欄で語をドラッグ選択 →「読みの追加」を押すと、このスクリプト内の
//   辞書(Seed)の末尾に「禁止|」を追記し、その行をIDEで開く。「|」の後ろに読みを書けば登録完了。
//   ひらがなを含む語は「禁ずる||v」（vは動詞・形容詞の活用語。不要なら|vを消す）。
//   読みが空の行は無視される。保存するとUnityが再コンパイルする。
//
//  ■ 保存方式（ScriptableObjectを使わない理由）
//   ScriptableObject/MonoBehaviour は「クラス名=ファイル名」が必須のため、1ファイルでは
//   ScriptableObjectアセットを別クラスで作れない。そのためルビ情報はこのコンポーネント自身に
//   シリアライズされ、シーン/Prefabと一緒に保存される。大量のセリフはマークアップ文字列
//   「{明日|あした}はいい{天気|てんき}です」に変換して保存できる（ToMarkup / SetTextWithRuby）。
//
//  ■ ランタイムAPI
//   SetTextWithRuby(string markup) / ToMarkup() / RebuildRuby() / showRuby
//
//  ■ 動作環境: Unity 2021.3以降 + TextMeshPro 3.0以降 / uGUI 2.0(Unity 6) を想定。
//    ※作成環境にUnityが無いため実機コンパイル・動作は未検証。エラー時はConsoleの内容を共有してください。
//
//  ■ ライセンス
//   このスクリプトは自由に利用可。辞書(IPAdic等)は各辞書のライセンスに従う
//   (mecab-ipadic: NAIST/ICOT系の寛容ライセンス, NEologd: Apache 2.0)。辞書ファイル自体は配布物に含めない。
// =====================================================================
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace AutoRuby
{
    public enum RubyAlign { Center = 0, Left = 1, Right = 2 }

    /// <summary>1つのルビ情報。本文とは独立して保持される。</summary>
    [Serializable]
    public class RubyEntry
    {
        public string id = Guid.NewGuid().ToString("N");
        public int start;                 // 本文(raw文字列)内の開始位置
        public int length;                // 対象文字数
        public string baseText = "";      // 対象の本文（同期検証用）
        public string reading = "";       // 読み
        public bool enabled = true;       // 表示ON/OFF（OFFでも読みは保持）
        public bool manual;               // 手動修正済み（再生成で保護）
        public float sizeScale = 0f;      // 0=全体設定を使用 / それ以外=本文サイズに対する比率
        public float widthScale = 1f;     // 横方向の倍率
        public float spacing = 0f;        // 文字間隔
        public RubyAlign align = RubyAlign.Center;
        public Vector2 offset;            // 位置微調整
        public int End { get { return start + length; } }
    }

    internal static class RubyChars
    {
        /// <summary>iの位置が漢字ならそのUTF-16長(1or2)、違えば0</summary>
        public static int KanjiLen(string s, int i)
        {
            if (s == null || i < 0 || i >= s.Length) return 0;
            char c = s[i];
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) ||
                (c >= 0xF900 && c <= 0xFAFF) || c == '々' || c == '〆') return 1;
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                int cp = char.ConvertToUtf32(c, s[i + 1]);
                if (cp >= 0x20000 && cp <= 0x2FA1F) return 2;
            }
            return 0;
        }

        public static bool IsHira(char c) { return c >= 0x3041 && c <= 0x309F; }

        public static string ToHira(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c >= 0x30A1 && c <= 0x30F6 ? (char)(c - 0x60) : c);
            return sb.ToString();
        }
    }

    // =================================================================
    //  メインコンポーネント（描画・同期・保存）
    // =================================================================
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("AutoRuby/Auto Ruby TMP")]
    public class AutoRubyTMP : MonoBehaviour
    {
        [Header("表示設定（全体）")]
        public bool showRuby = true;
        [Range(0.2f, 1f)] public float rubySizeRatio = 0.5f;      // ルビ文字サイズ/本文サイズ
        public float gap = 0f;                                    // ルビと本文の上下間隔
        public bool shrinkToFit = true;                           // ルビが長いとき横に縮小
        [Range(0.3f, 1f)] public float minHorizontalScale = 0.6f; // 縮小の下限

        [Header("Editor")]
        public bool autoGenerateOnEdit = true;                    // 本文編集時に新しい漢字へ自動生成
#if UNITY_EDITOR
        public TextAsset userDictionary;                          // ユーザー辞書(任意)
#endif

        [HideInInspector] public List<RubyEntry> entries = new List<RubyEntry>();
        [HideInInspector, SerializeField] string lastText = "";

        sealed class Seg
        {
            public int line, first, last, n;
            public float x0, x1, asc, size;
            public Color32 color;
        }

        const string RootName = "__AutoRubyRoot";
        const HideFlags Flags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;

        TMP_Text tmp;
        Transform root;
        readonly List<TMP_Text> pool = new List<TMP_Text>();
        readonly List<Seg> segs = new List<Seg>();
        bool dirty = true, building;
        int lastSig, version;

        public TMP_Text Tmp { get { if (tmp == null) tmp = GetComponent<TMP_Text>(); return tmp; } }

        // ---------------- lifecycle ----------------
        void OnEnable() { CleanupRoot(); dirty = true; }
        void OnDisable() { CleanupRoot(); }
        void OnValidate()
        {
            version++; dirty = true;
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void LateUpdate()
        {
            if (building) return;
            var t = Tmp;
            if (t == null) return;
            if (dirty || t.havePropertiesChanged || Signature() != lastSig)
            {
                SyncText();
                RebuildRuby();
            }
        }

        public void MarkDirty() { version++; dirty = true; }
        public void Refresh() { SyncText(); RebuildRuby(); }

        int Signature()
        {
            var t = Tmp;
            if (t == null) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + (t.text != null ? t.text.GetHashCode() : 0);
                h = h * 31 + t.fontSize.GetHashCode();
                var r = t.rectTransform.rect;
                h = h * 31 + r.width.GetHashCode();
                h = h * 31 + r.height.GetHashCode();
                h = h * 31 + t.maxVisibleCharacters;
                h = h * 31 + t.lineSpacing.GetHashCode();
                h = h * 31 + t.characterSpacing.GetHashCode();
                h = h * 31 + (int)t.alignment;
                h = h * 31 + t.color.GetHashCode();
                h = h * 31 + (t.font != null ? t.font.GetInstanceID() : 0);
                h = h * 31 + (int)t.fontStyle;
                h = h * 31 + (showRuby ? 1 : 0);
                h = h * 31 + rubySizeRatio.GetHashCode();
                h = h * 31 + gap.GetHashCode();
                h = h * 31 + minHorizontalScale.GetHashCode();
                h = h * 31 + (shrinkToFit ? 1 : 0);
                h = h * 31 + version;
                h = h * 31 + (isActiveAndEnabled ? 1 : 0);
                return h;
            }
        }

        // ---------------- 本文⇔ルビ 同期 ----------------
        /// <summary>
        /// 前回の本文と現在の本文の差分(共通の先頭/末尾を除いた変更範囲)を求め、
        /// 変更範囲より前のルビは維持、後ろのルビは位置をずらし、
        /// 変更範囲に掛かるルビは削除する。最後に「対象文字列が本文と一致するか」を検証する。
        /// </summary>
        public void SyncText()
        {
            var t = Tmp;
            if (t == null) return;
            string now = t.text ?? "";
            string old = lastText ?? "";
            if (now == old) return;

            int max = Math.Min(old.Length, now.Length);
            int p = 0;
            while (p < max && old[p] == now[p]) p++;
            int s = 0, maxS = max - p;
            while (s < maxS && old[old.Length - 1 - s] == now[now.Length - 1 - s]) s++;
            int oldChangedEnd = old.Length - s;
            int delta = now.Length - old.Length;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.End <= p) { /* 変更より前: そのまま */ }
                else if (e.start >= oldChangedEnd) e.start += delta;   // 変更より後ろ: ずらす
                else { entries.RemoveAt(i); continue; }                // 変更に掛かる: 削除
                if (e.length <= 0 || e.start < 0 || e.End > now.Length ||
                    string.CompareOrdinal(now, e.start, e.baseText, 0, e.length) != 0)
                    entries.RemoveAt(i);
            }
            lastText = now;
            version++;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (autoGenerateOnEdit) AutoGenerate(false, false);
                EditorUtility.SetDirty(this);
                PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            }
#endif
        }

        // ---------------- マークアップ入出力 ----------------
        /// <summary>「{漢字|よみ}」形式のマークアップから本文とルビを設定する（ランタイム用）。</summary>
        public void SetTextWithRuby(string markup)
        {
            var sb = new StringBuilder();
            var list = new List<RubyEntry>();
            int pos = 0;
            foreach (Match m in Regex.Matches(markup ?? "", @"\{([^{}|]+)\|([^{}|]*)\}"))
            {
                sb.Append(markup, pos, m.Index - pos);
                var e = new RubyEntry
                {
                    start = sb.Length,
                    length = m.Groups[1].Length,
                    baseText = m.Groups[1].Value,
                    reading = m.Groups[2].Value,
                    manual = true
                };
                sb.Append(m.Groups[1].Value);
                list.Add(e);
                pos = m.Index + m.Length;
            }
            if (markup != null) sb.Append(markup, pos, markup.Length - pos);
            entries = list;
            lastText = sb.ToString();
            Tmp.text = lastText;
            MarkDirty();
        }

        /// <summary>現在の本文+有効なルビを「{漢字|よみ}」形式にして返す。</summary>
        public string ToMarkup()
        {
            string text = Tmp.text ?? "";
            var list = new List<RubyEntry>();
            foreach (var e in entries)
                if (e.enabled && !string.IsNullOrEmpty(e.reading) && e.start >= 0 && e.End <= text.Length) list.Add(e);
            list.Sort((a, b) => a.start.CompareTo(b.start));
            var sb = new StringBuilder();
            int pos = 0;
            foreach (var e in list)
            {
                if (e.start < pos) continue;
                sb.Append(text, pos, e.start - pos);
                sb.Append('{').Append(text, e.start, e.length).Append('|').Append(e.reading).Append('}');
                pos = e.End;
            }
            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }

        // ---------------- 描画 ----------------
        public void RebuildRuby()
        {
            if (building) return;
            var t = Tmp;
            if (t == null) return;
            building = true;
            try
            {
                int used = 0;
                if (showRuby && isActiveAndEnabled && entries.Count > 0 && !string.IsNullOrEmpty(t.text))
                {
                    EnsureRoot();
                    t.ForceMeshUpdate();
                    used = Layout(t);
                }
                for (int i = used; i < pool.Count; i++)
                    if (pool[i] != null) pool[i].gameObject.SetActive(false);
            }
            finally
            {
                building = false;
                dirty = false;
                lastSig = Signature();
            }
        }

        /// <summary>TMPのTextInfoから本文の実際の描画位置を取得し、ルビを配置する。</summary>
        int Layout(TMP_Text t)
        {
            var info = t.textInfo;
            int count = info.characterCount;
            var cis = info.characterInfo;
            int used = 0;
            int maxVis = t.maxVisibleCharacters;

            foreach (var e in entries)
            {
                if (!e.enabled || string.IsNullOrEmpty(e.reading) || e.length <= 0) continue;
                segs.Clear();
                Seg cur = null;
                for (int i = 0; i < count; i++)
                {
                    var ci = cis[i];
                    if (ci.index < e.start || ci.index >= e.End) continue;
                    if (cur == null || cur.line != ci.lineNumber)
                    {
                        cur = new Seg
                        {
                            line = ci.lineNumber,
                            first = i,
                            last = i,
                            n = 0,
                            x0 = ci.origin,
                            x1 = ci.xAdvance,
                            asc = ci.ascender,
                            size = ci.pointSize,
                            color = ci.color
                        };
                        segs.Add(cur);
                    }
                    cur.x0 = Mathf.Min(cur.x0, ci.origin);
                    cur.x1 = Mathf.Max(cur.x1, ci.xAdvance);
                    cur.asc = Mathf.Max(cur.asc, ci.ascender);
                    cur.last = i;
                    cur.n++;
                }
                int total = 0;
                foreach (var sg in segs) total += sg.n;
                if (total == 0) continue;

                // 行をまたぐ場合は、文字数比で読みを分割して各行に配置
                string rd = e.reading;
                int cursor = 0;
                for (int k = 0; k < segs.Count; k++)
                {
                    var sg = segs[k];
                    int take = (k == segs.Count - 1) ? rd.Length - cursor
                                                      : Mathf.RoundToInt(rd.Length * (float)sg.n / total);
                    take = Mathf.Clamp(take, 0, rd.Length - cursor);
                    if (take == 0) continue;
                    string part = rd.Substring(cursor, take);
                    cursor += take;
                    used = PlaceRuby(used, t, e, sg, part, maxVis);
                }
            }
            return used;
        }

        int PlaceRuby(int used, TMP_Text t, RubyEntry e, Seg s, string part, int maxVis)
        {
            var r = GetRuby(used, t);
            float ratio = e.sizeScale > 0f ? e.sizeScale : rubySizeRatio;
            r.fontSize = Mathf.Max(1f, s.size * ratio);
            r.characterSpacing = e.spacing;
            r.color = s.color;
            r.text = part;

            Vector2 pref = r.GetPreferredValues(part);
            float scaleX = Mathf.Max(0.05f, e.widthScale);
            float baseW = Mathf.Max(0.01f, s.x1 - s.x0);
            float w = pref.x * scaleX;
            if (shrinkToFit && w > baseW)
            {
                scaleX *= Mathf.Max(minHorizontalScale, baseW / w);
                w = pref.x * scaleX;
            }

            float cx;
            switch (e.align)
            {
                case RubyAlign.Left: cx = s.x0 + w * 0.5f; break;
                case RubyAlign.Right: cx = s.x1 - w * 0.5f; break;
                default: cx = (s.x0 + s.x1) * 0.5f; break;   // 複数漢字は範囲全体の中央
            }

            var rt = r.rectTransform;
            rt.sizeDelta = new Vector2(Mathf.Max(1f, pref.x), Mathf.Max(1f, pref.y));
            rt.localScale = new Vector3(scaleX, 1f, 1f);
            rt.localPosition = new Vector3(cx + e.offset.x, s.asc + gap + e.offset.y, 0f);
            // タイプライター演出: 対象の最後の文字が表示されたらルビも表示
            r.gameObject.SetActive(s.last < maxVis);
            return used + 1;
        }

        TMP_Text GetRuby(int i, TMP_Text basis)
        {
            while (pool.Count <= i) pool.Add(null);
            var r = pool[i];
            if (r == null) { r = CreateRuby(basis); pool[i] = r; }
            if (r.font != basis.font) r.font = basis.font;
            if (r.fontSharedMaterial != basis.fontSharedMaterial) r.fontSharedMaterial = basis.fontSharedMaterial;
            r.fontStyle = basis.fontStyle & (FontStyles.Bold | FontStyles.Italic);
            r.gameObject.SetActive(true);
            return r;
        }

        TMP_Text CreateRuby(TMP_Text basis)
        {
            EnsureRoot();
            var go = new GameObject("ruby", typeof(RectTransform));
            go.hideFlags = Flags;
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            TMP_Text r;
            if (basis is TextMeshProUGUI) r = go.AddComponent<TextMeshProUGUI>();
            else r = go.AddComponent<TextMeshPro>();
            r.enableWordWrapping = false;
            r.overflowMode = TextOverflowModes.Overflow;
            r.enableAutoSizing = false;
            r.alignment = TextAlignmentOptions.Bottom;
            r.richText = false;
            r.margin = Vector4.zero;
            r.raycastTarget = false;
            var rt = r.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            return r;
        }

        void EnsureRoot()
        {
            if (root != null) return;
            var go = new GameObject(RootName, typeof(RectTransform));
            go.hideFlags = Flags;
            go.layer = gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.localPosition = Vector3.zero;
            rt.localScale = Vector3.one;
            root = rt;
        }

        void CleanupRoot()
        {
            pool.Clear();
            root = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var ch = transform.GetChild(i);
                if (ch.name != RootName) continue;
                if (Application.isPlaying) Destroy(ch.gameObject); else DestroyImmediate(ch.gameObject);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 自動生成。replaceAuto=true: 手動修正していないルビを作り直す（手動分は保護）。
        /// false: ルビが付いていない漢字にだけ追加する。
        /// </summary>
        public int AutoGenerate(bool replaceAuto, bool log)
        {
            string text = Tmp.text ?? "";
            if (replaceAuto) entries.RemoveAll(e => !e.manual);
            var covered = new bool[text.Length];
            foreach (var e in entries)
                for (int k = Mathf.Max(0, e.start); k < e.End && k < covered.Length; k++) covered[k] = true;
            var unknown = log ? new List<string>() : null;
            var gen = RubyAnalyzer.Analyze(text, covered, userDictionary, unknown);
            entries.AddRange(gen);
            entries.Sort((a, b) => a.start.CompareTo(b.start));
            MarkDirty();
            EditorUtility.SetDirty(this);
            if (log && unknown != null && unknown.Count > 0)
                Debug.LogWarning("[AutoRuby] 読みが見つからなかった漢字: " + string.Join(" ", unknown.ToArray()) +
                                 "\n→ ユーザー辞書への追加、またはIPAdic辞書の取り込みを行ってください。", this);
            return gen.Count;
        }
#endif
    }

#if UNITY_EDITOR
    // =================================================================
    //  読み解析（Editor専用）: 辞書の最長一致 + 動詞/形容詞の語幹 + かな混在の読み分割
    // =================================================================
    internal static class RubyAnalyzer
    {
        const char Mask = '\uE000';

        // 内蔵の最小辞書（"表記|読み" / 末尾 |v は活用語＝語幹を自動登録）
        const string Seed = @"
明日|あした
今日|きょう
昨日|きのう
今朝|けさ
今年|ことし
去年|きょねん
来年|らいねん
毎日|まいにち
天気|てんき
五右衛門|ごえもん
日の出|ひので
日の入り|ひのいり
大人|おとな
子供|こども
友達|ともだち
一人|ひとり
二人|ふたり
時間|じかん
学校|がっこう
先生|せんせい
世界|せかい
勇者|ゆうしゃ
魔王|まおう
魔法|まほう
王様|おうさま
仲間|なかま
冒険|ぼうけん
戦闘|せんとう
敵|てき
味方|みかた
怪物|かいぶつ
迷宮|めいきゅう
宝箱|たからばこ
登場|とうじょう
文章|ぶんしょう
会話|かいわ
説明|せつめい
景色|けしき
人間|にんげん
名前|なまえ
言葉|ことば
場所|ばしょ
世の中|よのなか
自分|じぶん
気持ち|きもち
本当|ほんとう
結局|けっきょく
少し|すこし
一緒|いっしょ
必要|ひつよう
大切|たいせつ
普通|ふつう
最後|さいご
最初|さいしょ
部屋|へや
彼女|かのじょ
今|いま
空|そら
海|うみ
山|やま
川|かわ
森|もり
城|しろ
村|むら
町|まち
国|くに
剣|けん
私|わたし
彼|かれ
君|きみ
力|ちから
心|こころ
夢|ゆめ
光|ひかり
闇|やみ
風|かぜ
水|みず
声|こえ
手|て
目|め
愛|あい
命|いのち
道|みち
家|いえ
行く|いく|v
食べる|たべる|v
見る|みる|v
出る|でる|v
入る|はいる|v
言う|いう|v
思う|おもう|v
知る|しる|v
話す|はなす|v
聞く|きく|v
書く|かく|v
読む|よむ|v
歩く|あるく|v
走る|はしる|v
戦う|たたかう|v
守る|まもる|v
倒す|たおす|v
使う|つかう|v
持つ|もつ|v
帰る|かえる|v
考える|かんがえる|v
生きる|いきる|v
死ぬ|しぬ|v
会う|あう|v
待つ|まつ|v
動く|うごく|v
助ける|たすける|v
逃げる|にげる|v
探す|さがす|v
見つける|みつける|v
作る|つくる|v
始める|はじめる|v
終わる|おわる|v
美しい|うつくしい|v
楽しい|たのしい|v
強い|つよい|v
弱い|よわい|v
大きい|おおきい|v
小さい|ちいさい|v
良い|よい|v
悲しい|かなしい|v
嬉しい|うれしい|v
怖い|こわい|v
----ここからゲーム内追加文字----
禁止|きんし
行動|こうどう
危ない|あぶない|v
";

        static readonly Dictionary<string, string> words = new Dictionary<string, string>();
        static readonly Dictionary<string, string> stems = new Dictionary<string, string>();
        static int maxLen = 1;
        static bool loaded;

        static string CachePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library/AutoRubyDict.tsv"); }
        }

        public static void Reload() { loaded = false; EnsureLoaded(); }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            words.Clear(); stems.Clear(); maxLen = 1;
            try
            {
                if (File.Exists(CachePath))
                {
                    foreach (var line in File.ReadLines(CachePath))
                    {
                        var p = line.Split('\t');
                        if (p.Length < 3) continue;
                        if (p[0] == "W") { words[p[1]] = p[2]; if (p[1].Length > maxLen) maxLen = Math.Min(p[1].Length, 24); }
                        else if (p[0] == "S") stems[p[1]] = p[2];
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[AutoRuby] 辞書キャッシュの読込に失敗: " + ex.Message); }

            var seen = new HashSet<string>();
            foreach (var raw in Seed.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var p = line.Split('|');
                if (p.Length < 2 || p[1].Trim().Length == 0) continue;   // 読み未記入の行(「禁止|」)は無視
                AddWord(p[0], p[1].Trim(), p.Length >= 3 && p[2].Trim() == "v", seen);
            }
        }

        static void AddWord(string surface, string reading, bool inflect, HashSet<string> seen)
        {
            words[surface] = reading;
            if (surface.Length > maxLen) maxLen = Math.Min(surface.Length, 24);
            if (!inflect) return;
            string st, sr;
            if (TryStem(surface, reading, out st, out sr) && (seen == null || seen.Add(st))) stems[st] = sr;
        }

        /// <summary>「行く/いく」→ 語幹「行」/「い」。語幹は全て漢字、語尾はひらがなで読みの末尾と一致する場合のみ。</summary>
        static bool TryStem(string surface, string reading, out string stem, out string stemReading)
        {
            stem = null; stemReading = null;
            int t = surface.Length;
            while (t > 0 && RubyChars.IsHira(surface[t - 1])) t--;
            if (t == 0 || t == surface.Length) return false;
            string tail = surface.Substring(t);
            string st = surface.Substring(0, t);
            for (int k = 0; k < st.Length;)
            {
                int kl = RubyChars.KanjiLen(st, k);
                if (kl == 0) return false;
                k += kl;
            }
            if (reading.Length <= tail.Length || !reading.EndsWith(tail, StringComparison.Ordinal)) return false;
            stem = st;
            stemReading = reading.Substring(0, reading.Length - tail.Length);
            return true;
        }

        static Dictionary<string, string> ParseUser(TextAsset ta)
        {
            var d = new Dictionary<string, string>();
            if (ta == null) return d;
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.Trim('\r', ' ');
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split('\t', ',', '|');
                if (p.Length < 2) continue;
                string k = p[0].Trim(), v = RubyChars.ToHira(p[1].Trim());
                if (k.Length > 0 && v.Length > 0) d[k] = v;
            }
            return d;
        }

        static string MaskTags(string raw)
        {
            return Regex.Replace(raw, "<[^<>]*>", m => new string(Mask, m.Length));
        }

        static RubyEntry Make(int start, string text, string reading)
        {
            return new RubyEntry { start = start, length = text.Length, baseText = text, reading = reading };
        }

        public static List<RubyEntry> Analyze(string raw, bool[] covered, TextAsset userDict, List<string> unknown)
        {
            EnsureLoaded();
            var result = new List<RubyEntry>();
            if (string.IsNullOrEmpty(raw)) return result;

            var user = ParseUser(userDict);
            int maxL = maxLen;
            foreach (var k in user.Keys) if (k.Length > maxL) maxL = Math.Min(k.Length, 24);

            string s = MaskTags(raw);
            var found = new List<RubyEntry>();
            int i = 0;
            while (i < s.Length)
            {
                int kl = RubyChars.KanjiLen(s, i);
                if (kl == 0) { i++; continue; }

                bool ok = false;
                // (1) 辞書の最長一致（送り仮名・かな混在を含む語）
                int limit = Math.Min(maxL, s.Length - i);
                for (int L = limit; L >= 1 && !ok; L--)
                {
                    if (char.IsHighSurrogate(s[i + L - 1])) continue;
                    string sub = s.Substring(i, L);
                    if (sub.IndexOf(Mask) >= 0 || sub.IndexOf('\n') >= 0) continue;
                    string rd;
                    if (!user.TryGetValue(sub, out rd) && !words.TryGetValue(sub, out rd)) continue;
                    Align(sub, rd, i, found);
                    i += L;
                    ok = true;
                }
                if (ok) continue;

                // (2) 活用語の語幹（直後がひらがなの場合のみ）
                int run = 0;
                while (true) { int k = RubyChars.KanjiLen(s, i + run); if (k == 0) break; run += k; }
                for (int L = run; L >= 1 && !ok; L--)
                {
                    if (char.IsHighSurrogate(s[i + L - 1])) continue;
                    if (i + L >= s.Length || !RubyChars.IsHira(s[i + L])) continue;
                    string stem = s.Substring(i, L), sr;
                    if (stems.TryGetValue(stem, out sr))
                    {
                        found.Add(Make(i, stem, sr));
                        i += L;
                        ok = true;
                    }
                }
                if (ok) continue;

                if (unknown != null) unknown.Add(s.Substring(i, kl));
                i += kl;
            }

            foreach (var e in found)
            {
                bool overlap = false;
                for (int k = e.start; k < e.End && k < covered.Length; k++) if (covered[k]) { overlap = true; break; }
                if (!overlap) result.Add(e);
            }
            return result;
        }

        /// <summary>
        /// 表記と読みを「漢字の塊」と「かな」に分けて対応付け、漢字の塊だけにルビを作る。
        /// 例: 日の出/ひので → [日:ひ][出:で]。五右衛門/ごえもん → 1つの塊(全体中央)。
        /// 対応付けに失敗したら安全策として語全体に1つ付ける。
        /// </summary>
        static void Align(string surface, string reading, int abs, List<RubyEntry> outList)
        {
            var gs = new List<int[]>();   // {start, len, isKanji}
            int i = 0;
            while (i < surface.Length)
            {
                bool kj = RubyChars.KanjiLen(surface, i) > 0;
                int st = i;
                while (i < surface.Length)
                {
                    int kk = RubyChars.KanjiLen(surface, i);
                    if ((kk > 0) != kj) break;
                    i += kk > 0 ? kk : 1;
                }
                gs.Add(new[] { st, i - st, kj ? 1 : 0 });
            }
            if (gs.Count == 1 && gs[0][2] == 1) { outList.Add(Make(abs, surface, reading)); return; }

            var tmp = new List<RubyEntry>();
            int ri = 0;
            bool okAlign = true;
            for (int g = 0; g < gs.Count && okAlign; g++)
            {
                string seg = surface.Substring(gs[g][0], gs[g][1]);
                if (gs[g][2] == 0)
                {
                    string lit = RubyChars.ToHira(seg);
                    if (ri + lit.Length <= reading.Length && string.CompareOrdinal(reading, ri, lit, 0, lit.Length) == 0) ri += lit.Length;
                    else okAlign = false;
                }
                else
                {
                    int end;
                    if (g == gs.Count - 1) end = reading.Length;
                    else
                    {
                        string nl = RubyChars.ToHira(surface.Substring(gs[g + 1][0], gs[g + 1][1]));
                        if (g + 1 == gs.Count - 1) end = reading.EndsWith(nl, StringComparison.Ordinal) ? reading.Length - nl.Length : -1;
                        else end = reading.IndexOf(nl, ri + 1, StringComparison.Ordinal);
                    }
                    if (end <= ri) okAlign = false;
                    else { tmp.Add(Make(abs + gs[g][0], seg, reading.Substring(ri, end - ri))); ri = end; }
                }
            }
            if (okAlign && ri == reading.Length) outList.AddRange(tmp);
            else outList.Add(Make(abs, surface, reading));
        }

        // ---------------- MeCab-IPAdic CSV 取り込み ----------------
        [MenuItem("Tools/AutoRuby/Import MeCab-IPAdic CSV Folder...")]
        static void ImportMenu()
        {
            string dir = EditorUtility.OpenFolderPanel("mecab-ipadic の *.csv があるフォルダ", "", "");
            if (!string.IsNullOrEmpty(dir)) Import(dir);
        }

        static string Decode(byte[] bytes)
        {
            string s = new UTF8Encoding(false, false).GetString(bytes);
            if (s.IndexOf('\uFFFD') < 0) return s;
            try { return Encoding.GetEncoding("euc-jp").GetString(bytes); }
            catch (Exception) { Debug.LogWarning("[AutoRuby] EUC-JPを読めません。CSVをUTF-8に変換してください。"); return s; }
        }

        public static void Import(string dir)
        {
            var costW = new Dictionary<string, int>();
            var rdW = new Dictionary<string, string>();
            var costS = new Dictionary<string, int>();
            var rdS = new Dictionary<string, string>();
            try
            {
                var files = Directory.GetFiles(dir, "*.csv");
                if (files.Length == 0) { Debug.LogWarning("[AutoRuby] CSVが見つかりません: " + dir); return; }
                for (int f = 0; f < files.Length; f++)
                {
                    EditorUtility.DisplayProgressBar("AutoRuby 辞書取り込み", Path.GetFileName(files[f]), (float)f / files.Length);
                    string txt = Decode(File.ReadAllBytes(files[f]));
                    foreach (var raw in txt.Split('\n'))
                    {
                        var line = raw.TrimEnd('\r');
                        if (line.Length == 0 || line.IndexOf('"') >= 0) continue;
                        var p = line.Split(',');
                        if (p.Length < 13) continue;
                        string surface = p[0];
                        if (surface.Length == 0 || surface.Length > 16 || RubyChars.KanjiLen(surface, 0) == 0) continue;
                        string rd = p[11];
                        if (rd == "*" || rd.Length == 0) continue;
                        rd = RubyChars.ToHira(rd);
                        int cost; int.TryParse(p[3], out cost);
                        int c;
                        if (!costW.TryGetValue(surface, out c) || cost < c) { costW[surface] = cost; rdW[surface] = rd; }
                        if (p[4] == "動詞" || p[4] == "形容詞")
                        {
                            string st, sr;
                            if (TryStem(surface, rd, out st, out sr) && (!costS.TryGetValue(st, out c) || cost < c))
                            { costS[st] = cost; rdS[st] = sr; }
                        }
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                using (var w = new StreamWriter(CachePath, false, new UTF8Encoding(false)))
                {
                    foreach (var kv in rdW) w.WriteLine("W\t" + kv.Key + "\t" + kv.Value);
                    foreach (var kv in rdS) w.WriteLine("S\t" + kv.Key + "\t" + kv.Value);
                }
                Reload();
                Debug.Log("[AutoRuby] 辞書を取り込みました: 語 " + rdW.Count + " / 語幹 " + rdS.Count);
            }
            catch (Exception ex) { Debug.LogError("[AutoRuby] 取り込み失敗: " + ex); }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }

    // =================================================================
    //  編集UI（Inspector / Ruby Text Editor ウィンドウ共通）
    // =================================================================
    internal static class RubyEditorGUI
    {
        static readonly HashSet<string> open = new HashSet<string>();

        const string BodyControl = "AutoRubyBody";
        static int selOwner;
        static string selText = "";

        static System.Reflection.FieldInfo editorField;
        static bool editorFieldSearched;

        /// <summary>
        /// 現在フォーカス中のテキスト欄のエディタ(選択範囲を持つ)。
        /// EditorGUIのテキスト欄は内部の共有エディタを使うため、リフレクションで取得する。
        /// </summary>
        static TextEditor ActiveEditor()
        {
            if (!editorFieldSearched)
            {
                editorFieldSearched = true;
                var bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                editorField = typeof(EditorGUI).GetField("activeEditor", bf);
                if (editorField == null) editorField = typeof(EditorGUI).GetField("s_RecycledEditor", bf);
            }
            return editorField != null ? editorField.GetValue(null) as TextEditor : null;
        }

        /// <summary>①本文欄で選択中の文字列を覚えておく（ボタンを押すと欄のフォーカスが外れても使えるように）。</summary>
        static void CacheSelection(AutoRubyTMP c)
        {
            if (GUI.GetNameOfFocusedControl() != BodyControl) return;
            var te = ActiveEditor();
            if (te == null || te.text == null) return;
            int len = te.text.Length;
            int a = Mathf.Clamp(Mathf.Min(te.cursorIndex, te.selectIndex), 0, len);
            int b = Mathf.Clamp(Mathf.Max(te.cursorIndex, te.selectIndex), 0, len);
            selOwner = c.GetInstanceID();
            selText = te.text.Substring(a, b - a);
        }

        /// <summary>このコンポーネントのスクリプトファイルを探す（複数の方法で確実に）。</summary>
        static MonoScript FindScript(AutoRubyTMP c)
        {
            MonoScript ms = MonoScript.FromMonoBehaviour(c);
            if (ms != null) return ms;
            var prop = new SerializedObject(c).FindProperty("m_Script");
            if (prop != null) ms = prop.objectReferenceValue as MonoScript;
            if (ms != null) return ms;
            foreach (var guid in AssetDatabase.FindAssets("AutoRubyTMP t:MonoScript"))
            {
                var m = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.GetClass() == typeof(AutoRubyTMP)) return m;
            }
            return null;
        }

        static void Warn(string msg)
        {
            EditorUtility.DisplayDialog("読みの追加", msg, "OK");
            GUIUtility.ExitGUI();
        }

        static int CountLines(string s, int upTo)
        {
            int n = 1;
            for (int i = 0; i < upTo && i < s.Length; i++) if (s[i] == '\n') n++;
            return n;
        }

        /// <summary>
        /// 選択した語を、このスクリプト内蔵辞書(Seed)の末尾に追記し、その行をエディタで開く。
        /// 漢字のみ→「禁止|」 / ひらがなを含む→「禁ずる||v」。読みは開いたスクリプト上で書き足す。
        /// </summary>
        static void AddReadingFromSelection(AutoRubyTMP c)
        {
            string sel = (selOwner == c.GetInstanceID() ? selText : "").Trim();
            // 保険: このUnityで選択範囲を取得できない場合は、Ctrl+Cでコピーした語を使う
            if (sel.Length == 0 && editorFieldSearched && editorField == null)
                sel = (GUIUtility.systemCopyBuffer ?? "").Trim();
            if (sel.Length == 0) { Warn("①の本文欄で、読みを追加したい語をドラッグで選択してから押してください。"); return; }
            if (sel.Length > 24 || sel.IndexOfAny(new[] { '|', '\n', '\r', '<', '>' }) >= 0)
            { Warn("選択した範囲が長すぎるか、改行・記号(| < >)を含んでいます。語だけを選択してください。"); return; }

            bool hasKanji = false, hasHira = false;
            for (int i = 0; i < sel.Length;)
            {
                int k = RubyChars.KanjiLen(sel, i);
                if (k > 0) { hasKanji = true; i += k; }
                else { if (RubyChars.IsHira(sel[i])) hasHira = true; i++; }
            }
            if (!hasKanji) { Warn("選択範囲に漢字が含まれていません。"); return; }

            var ms = FindScript(c);
            string path = ms != null ? AssetDatabase.GetAssetPath(ms) : null;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
            { Warn("スクリプトの場所を特定できませんでした。\n見つかったパス: " + (string.IsNullOrEmpty(path) ? "（なし）" : path)); return; }

            byte[] bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string src = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

            string key = "const string " + "Seed = @" + "\"";
            int s = src.IndexOf(key, StringComparison.Ordinal);
            int e = s < 0 ? -1 : src.IndexOf("\n\";", s, StringComparison.Ordinal);
            if (s < 0 || e < 0)
            { Warn("内蔵辞書(Seed)の終わりを見つけられませんでした。辞書の終わりが「\";」だけの行になっているか確認してください。"); return; }

            // すでに登録済みなら、その行を開くだけ
            string[] seedLines = src.Substring(s, e - s).Split('\n');
            string prefix = sel + "|";
            int baseLine = CountLines(src, s);
            for (int i = 0; i < seedLines.Length; i++)
            {
                if (!seedLines[i].Trim().StartsWith(prefix, StringComparison.Ordinal)) continue;
                int dupLine = baseLine + i;
                EditorUtility.DisplayDialog("読みの追加", "「" + sel + "」はすでに辞書にあります（" + dupLine + "行目）。その行を開きます。", "OK");
                AssetDatabase.OpenAsset(ms, dupLine);
                GUIUtility.ExitGUI();
                return;
            }

            string nl = src.Contains("\r\n") ? "\r\n" : "\n";
            string line = hasHira ? sel + "||v" : sel + "|";
            string result = src.Insert(e + 1, line + nl);
            int newLine = CountLines(result, e + 1);
            File.WriteAllText(path, result, new UTF8Encoding(bom));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Debug.Log("[AutoRuby] 辞書に追加しました（" + newLine + "行目）: " + line + "  → 「|」の後ろに読みを書き足してください。");
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(path), newLine);
            GUIUtility.ExitGUI();
        }

        public static void Draw(AutoRubyTMP c)
        {
            var t = c.GetComponent<TMP_Text>();
            if (t == null) return;

            // ① 本文
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("① 本文", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName(BodyControl);
            string nt = EditorGUILayout.TextArea(t.text, GUILayout.MinHeight(54));
            CacheSelection(c);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(new UnityEngine.Object[] { t, c }, "Edit Ruby Text");
                t.text = nt;
                c.Refresh();
                EditorUtility.SetDirty(t);
                EditorUtility.SetDirty(c);
            }

            // ② 自動生成
            EditorGUILayout.LabelField("② 自動生成", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("未設定の漢字に生成"))
            { Undo.RecordObject(c, "Ruby Generate"); c.AutoGenerate(false, true); c.Refresh(); }
            if (GUILayout.Button("自動分を再生成(手動は保護)"))
            { Undo.RecordObject(c, "Ruby Regenerate"); c.AutoGenerate(true, true); c.Refresh(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全削除して再生成"))
            { Undo.RecordObject(c, "Ruby Reset"); c.entries.Clear(); c.AutoGenerate(false, true); c.Refresh(); }
            if (GUILayout.Button("ルビを全削除"))
            { Undo.RecordObject(c, "Ruby Clear"); c.entries.Clear(); c.MarkDirty(); c.Refresh(); EditorUtility.SetDirty(c); }
            EditorGUILayout.EndHorizontal();
            string selNow = (selOwner == c.GetInstanceID()) ? selText : "";
            EditorGUILayout.LabelField("選択中の語: " + (selNow.Length > 0 ? "「" + selNow + "」" : "（①の本文欄をドラッグで選択）"), EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("読みの追加（選択した語を辞書へ）")) AddReadingFromSelection(c);
            if (GUILayout.Button("マークアップを貼り付け"))
            { Undo.RecordObjects(new UnityEngine.Object[] { t, c }, "Ruby Paste"); c.SetTextWithRuby(GUIUtility.systemCopyBuffer); c.Refresh(); EditorUtility.SetDirty(c); EditorUtility.SetDirty(t); }
            EditorGUILayout.EndHorizontal();

            // ③ ルビ一覧
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("③ ルビ一覧（本文は変更されません）  ☑=表示 / ⚙=詳細 / ×=削除", EditorStyles.boldLabel);
            var so = new SerializedObject(c);
            so.Update();
            var list = so.FindProperty("entries");
            if (list.arraySize == 0) EditorGUILayout.HelpBox("ルビがありません。本文に漢字を入力するか、上の生成ボタンを押してください。", MessageType.None);

            for (int i = 0; i < list.arraySize; i++)
            {
                var p = list.GetArrayElementAtIndex(i);
                var pEn = p.FindPropertyRelative("enabled");
                var pBase = p.FindPropertyRelative("baseText");
                var pRead = p.FindPropertyRelative("reading");
                var pManual = p.FindPropertyRelative("manual");
                string id = p.FindPropertyRelative("id").stringValue;

                EditorGUILayout.BeginHorizontal();
                pEn.boolValue = EditorGUILayout.Toggle(pEn.boolValue, GUILayout.Width(16));
                GUILayout.Label(pBase.stringValue, EditorStyles.helpBox, GUILayout.Width(100));
                EditorGUI.BeginChangeCheck();
                string nr = EditorGUILayout.TextField(pRead.stringValue);
                if (EditorGUI.EndChangeCheck()) { pRead.stringValue = nr; pManual.boolValue = true; }
                GUILayout.Label(pManual.boolValue ? "✎" : " ", GUILayout.Width(16));
                bool ex = open.Contains(id);
                bool nex = GUILayout.Toggle(ex, "⚙", EditorStyles.miniButton, GUILayout.Width(24));
                if (nex != ex) { if (nex) open.Add(id); else open.Remove(id); }
                bool del = GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22));
                EditorGUILayout.EndHorizontal();

                if (nex)
                {
                    EditorGUI.indentLevel++;
                    var pSize = p.FindPropertyRelative("sizeScale");
                    pSize.floatValue = EditorGUILayout.Slider(new GUIContent("文字サイズ比 (0=全体設定)"), pSize.floatValue, 0f, 1f);
                    var pW = p.FindPropertyRelative("widthScale");
                    pW.floatValue = EditorGUILayout.Slider("文字幅(横倍率)", pW.floatValue, 0.3f, 2f);
                    EditorGUILayout.PropertyField(p.FindPropertyRelative("spacing"), new GUIContent("文字間隔"));
                    EditorGUILayout.PropertyField(p.FindPropertyRelative("align"), new GUIContent("配置"));
                    EditorGUILayout.PropertyField(p.FindPropertyRelative("offset"), new GUIContent("位置オフセット"));
                    EditorGUI.indentLevel--;
                }
                if (del) { list.DeleteArrayElementAtIndex(i); break; }
            }
            if (so.ApplyModifiedProperties())
            {
                c.MarkDirty();
                EditorUtility.SetDirty(c);
                c.Refresh();
            }
        }
    }

    [CustomEditor(typeof(AutoRubyTMP))]
    internal class AutoRubyTMPInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            RubyEditorGUI.Draw((AutoRubyTMP)target);
        }
    }

    public class RubyTextEditorWindow : EditorWindow
    {
        Vector2 scroll;

        [MenuItem("Tools/AutoRuby/Ruby Text Editor")]
        static void Open() { GetWindow<RubyTextEditorWindow>("Ruby Text Editor"); }

        void OnSelectionChange() { Repaint(); }

        void OnGUI()
        {
            var go = Selection.activeGameObject;
            var c = go != null ? go.GetComponent<AutoRubyTMP>() : null;
            if (c == null)
            {
                EditorGUILayout.HelpBox("ルビ機能を付けたTMPテキストをHierarchyで選択してください。", MessageType.Info);
                if (go != null && go.GetComponent<TMP_Text>() != null && GUILayout.Button("このテキストにルビ機能を追加"))
                    Undo.AddComponent<AutoRubyTMP>(go);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(go.name, EditorStyles.boldLabel);
            RubyEditorGUI.Draw(c);
            EditorGUILayout.EndScrollView();
        }
    }

    internal static class RubyMenu
    {
        [MenuItem("CONTEXT/TextMeshProUGUI/ルビ機能を追加 (AutoRuby)")]
        static void AddUGUI(MenuCommand cmd) { Add(((Component)cmd.context).gameObject); }

        [MenuItem("CONTEXT/TextMeshPro/ルビ機能を追加 (AutoRuby)")]
        static void Add3D(MenuCommand cmd) { Add(((Component)cmd.context).gameObject); }

        static void Add(GameObject go)
        {
            if (go.GetComponent<AutoRubyTMP>() == null) Undo.AddComponent<AutoRubyTMP>(go);
        }

        [MenuItem("Tools/AutoRuby/Create Sample Objects")]
        static void CreateSample()
        {
            var cgo = new GameObject("RubySampleCanvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            cgo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            string[] samples =
            {
                "明日はいい天気です。",
                "日の出がきれいです。",
                "五右衛門が登場します。",
                "行きました。",
                "折り返しテスト：勇者は魔王の城へ向かい、仲間と一緒に冒険を続けました。明日も天気がよいでしょう。"
            };
            for (int i = 0; i < samples.Length; i++)
            {
                var go = new GameObject("Sample" + (i + 1), typeof(RectTransform));
                go.transform.SetParent(cgo.transform, false);
                var t = go.AddComponent<TextMeshProUGUI>();
                t.text = samples[i];
                t.fontSize = 40;
                t.lineSpacing = 40;   // ルビの分だけ行間を空ける
                t.color = Color.white;
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(700, i == samples.Length - 1 ? 220 : 80);
                rt.anchoredPosition = new Vector2(0, -40 - i * 100);
                var c = go.AddComponent<AutoRubyTMP>();
                c.Refresh();
            }
            Undo.RegisterCreatedObjectUndo(cgo, "Create Ruby Sample");
            Selection.activeGameObject = cgo;
            Debug.Log("[AutoRuby] サンプルを作成しました。漢字が表示されない場合は、日本語対応のTMPフォントアセットを各テキストに設定してください。");
        }
    }
#endif
}