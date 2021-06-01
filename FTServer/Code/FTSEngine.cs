﻿/* iBoxDB FTServer Bruce Yang CL */
using System;
using System.Collections.Generic;
using System.Text;
using IBoxDB.LocalServer;
using System.Collections.Concurrent;
using System.Threading;

namespace FTServer
{
    public class Engine
    {

        internal readonly StringUtil sUtil = new StringUtil();

        public void Config(DatabaseConfig config)
        {
            KeyWord.config(config);
        }
        public bool isWord(char c)
        {
            return sUtil.isWord(c);
        }

        public long indexText(IBox box, long id, String str, bool isRemove, ThreadStart delay = null)
        {
            if (id == -1)
            {
                return -1;
            }
            long itCount = 0;
            char[] cs = sUtil.clear(str);
            List<KeyWord> map = sUtil.fromString(id, cs, true);


            foreach (KeyWord kw in map)
            {
                delay?.Invoke();
                insertToBox(box, kw, isRemove);
                itCount++;
            }
            return itCount;
        }

        public long indexTextNoTran(AutoBox auto, int commitCount, long id, String str, bool isRemove)
        {
            if (id == -1)
            {
                return -1;
            }
            long itCount = 0;
            char[] cs = sUtil.clear(str);
            List<KeyWord> map = sUtil.fromString(id, cs, true);


            IBox box = null;
            int ccount = 0;
            foreach (KeyWord kw in map)
            {
                if (box == null)
                {
                    box = auto.Cube();
                    ccount = commitCount;
                }
                insertToBox(box, kw, isRemove);
                itCount++;
                if (--ccount < 1)
                {
                    box.Commit().Assert();
                    box = null;
                }
            }
            if (box != null)
            {
                box.Commit().Assert();
            }
            return itCount;
        }

        static void insertToBox(IBox box, KeyWord kw, bool isRemove)
        {
            Binder binder;
            if (kw is KeyWordE)
            {
                binder = box["/E", kw.getKeyWord(), kw.I, kw.P];
            }
            else
            {
                binder = box["/N", kw.getKeyWord(), kw.I, kw.P];
            }
            if (isRemove)
            {
                binder.Delete();
            }
            else
            {
                if (binder.TableName == "/E")
                {
                    binder.Insert((KeyWordE)kw);
                }
                else
                {
                    binder.Insert((KeyWordN)kw);
                }
            }
        }

        public SortedSet<String> discover(IBox box,
                                            char efrom, char eto, int elength,
                                            char nfrom, char nto, int nlength)
        {
            SortedSet<String> list = new SortedSet<String>();
            Random ran = new Random();
            if (elength > 0)
            {
                int len = ran.Next(KeyWord.MAX_WORD_LENGTH) + 1;
                char[] cs = new char[len];
                for (int i = 0; i < cs.Length; i++)
                {
                    cs[i] = (char)(ran.Next(eto - efrom) + efrom);
                }
                KeyWordE kw = new KeyWordE();
                kw.setKeyWord(new String(cs));
                foreach (KeyWord tkw in lessMatch(box, kw))
                {
                    String str = tkw.getKeyWord().ToString();
                    if (str[0] < efrom)
                    {
                        break;
                    }
                    int c = list.Count;
                    list.Add(str);
                    if (list.Count > c)
                    {
                        elength--;
                        if (elength <= 0)
                        {
                            break;
                        }
                    }
                }
            }
            if (nlength > 0)
            {
                char[] cs = new char[2];
                for (int i = 0; i < cs.Length; i++)
                {
                    cs[i] = (char)(ran.Next(nto - nfrom) + nfrom);
                }
                KeyWordN kw = new KeyWordN();
                kw.longKeyWord(cs[0], cs[1], (char)0);
                foreach (KeyWord tkw in lessMatch(box, kw))
                {
                    String str = ((KeyWordN)tkw).toKString();
                    if (str[0] < nfrom)
                    {
                        break;
                    }
                    int c = list.Count;
                    list.Add(str);
                    if (list.Count > c)
                    {
                        nlength--;
                        if (nlength <= 0)
                        {
                            break;
                        }
                    }
                }
            }
            return list;
        }

        public IEnumerable<KeyWord> searchDistinct(IBox box, String str)
        {
            return searchDistinct(box, str, long.MaxValue, long.MaxValue);
        }
        // startId -> descending order
        public IEnumerable<KeyWord> searchDistinct(IBox box, String str, long startId, long len)
        {
            long c_id = -1;
            foreach (KeyWord kw in search(box, str, startId))
            {
                if (len < 1)
                {
                    break;
                }
                if (kw.I == c_id)
                {
                    continue;
                }
                c_id = kw.I;
                len--;
                yield return kw;
            }
        }

        public String getDesc(String str, KeyWord kw, int length)
        {
            return sUtil.getDesc(str, kw, length);
        }

        public IEnumerable<KeyWord> search(IBox box, String str)
        {
            return search(box, str, long.MaxValue);
        }

        public IEnumerable<KeyWord> search(IBox box, String str, long startId)
        {
            if (startId < 0)
            {
                return new ArrayList<KeyWord>();
            }
            char[] cs = sUtil.clear(str);
            ArrayList<KeyWord> map = sUtil.fromString(-1, cs, false);

            if (map.size() > KeyWord.MAX_WORD_LENGTH || map.isEmpty())
            {
                return new ArrayList<KeyWord>();
            }

            MaxID maxId = new MaxID();
            maxId.id = startId;
            return search(box, map.ToArray(), maxId);
        }

        private IEnumerable<KeyWord> search(IBox box, KeyWord[] kws, MaxID maxId)
        {

            if (kws.Length == 1)
            {
                return search(box, kws[0], (KeyWord)null, maxId);
            }

            return search(box, kws[kws.Length - 1],
                           search(box, Arrays.copyOf(kws, kws.Length - 1), maxId),
                           maxId);
        }

        private IEnumerable<KeyWord> search(IBox box, KeyWord nw,
                                             IEnumerable<KeyWord> condition, MaxID maxId)
        {
            IEnumerator<KeyWord> cd = condition.GetEnumerator();

            IEnumerator<KeyWord> r1 = null;

            KeyWord r1_con = null;
            long r1_id = -1;
            return new Iterable<KeyWord>()
            {

                iterator = new EngineIterator<KeyWord>()
                {


                    hasNext = () =>
                    {
                        if (r1 != null && r1.MoveNext())
                        {
                            return true;
                        }
                        while (cd.MoveNext())
                        {
                            r1_con = cd.Current;

                            if (r1_id == r1_con.I)
                            {
                                continue;
                            }
                            if (!nw.isLinked)
                            {
                                r1_id = r1_con.I;
                            }

                            r1 = search(box, nw, r1_con, maxId).GetEnumerator();
                            if (r1.MoveNext())
                            {
                                return true;
                            }

                        }
                        return false;
                    },

                    next = () =>
                    {
                        KeyWord k = r1.Current;
                        k.previous = r1_con;
                        return k;
                    }
                }

            };

        }

        private static IEnumerable<KeyWord> search(IBox box,
                                                    KeyWord kw, KeyWord con, MaxID maxId)
        {

            String ql = kw is KeyWordE
                ? "from /E where K==? & I<=?"
                    : "from /N where K==? & I<=?";


            int linkPos = kw.isLinked ? (con.P + con.size()
                + (kw is KeyWordE ? 1 : 0)) : -1;

            long currentMaxId = long.MaxValue;
            KeyWord cache = null;
            IEnumerator<KeyWord> iter = null;
            bool isLinkEndMet = false;

            return new Iterable<KeyWord>()
            {
                iterator = new EngineIterator<KeyWord>()
                {


                    hasNext = () =>
                    {
                        if (maxId.id == -1)
                        {
                            return false;
                        }

                        if (currentMaxId > (maxId.id + 1))
                        {
                            currentMaxId = maxId.id;
                            iter = kw is KeyWordE ?
                                (IEnumerator<KeyWord>)box.Select<KeyWordE>(ql, kw.getKeyWord(), maxId.id).GetEnumerator() :
                                    box.Select<KeyWordN>(ql, kw.getKeyWord(), maxId.id).GetEnumerator();
                        }

                        while (iter.MoveNext())
                        {

                            cache = iter.Current;

                            maxId.id = cache.I;
                            currentMaxId = maxId.id;
                            if (con != null && con.I != maxId.id)
                            {
                                return false;
                            }

                            if (isLinkEndMet)
                            {
                                continue;
                            }

                            if (linkPos == -1)
                            {
                                return true;
                            }

                            int cpos = cache.P;
                            if (cpos > linkPos)
                            {
                                continue;
                            }
                            if (cpos == linkPos)
                            {
                                if (kw.isLinkedEnd)
                                {
                                    isLinkEndMet = true;
                                }
                                return true;
                            }
                            return false;
                        }

                        maxId.id = -1;
                        return false;

                    },

                    next = () =>
                    {
                        return cache;
                    }

                }
            };


        }

        private static IEnumerable<KeyWord> lessMatch(IBox box, KeyWord kw)
        {
            if (kw is KeyWordE)
            {
                return box.Select<KeyWordE>("from /E where K<=? limit 0, 50", kw.getKeyWord());

            }
            else
            {
                return box.Select<KeyWordN>("from /N where K<=? limit 0, 50", kw.getKeyWord());
            }
        }

        private sealed class MaxID
        {
            public long id = long.MaxValue;
        }
    }
    #region StringUtil
    class StringUtil
    {
        HashSet<Char> set;

        public StringUtil()
        {

            String s = "!\"@$%&'()*+,./:;<=>?[\\]^_`{|}~\r\n"; //@-
            s += "， 　，《。》、？；：‘’“”【｛】｝——=+、｜·～！￥%……&*（）"; //@-#
            s += "｀～！＠￥％……—×（）——＋－＝【】｛｝：；’＇”＂，．／＜＞？’‘”“";//＃
            s += "� ★☆,。？,　！";
            s += "©»¥「」";
            s += "[¡, !, \", ', (, ), -, °, :, ;, ?]-\"#";

            set = new HashSet<Char>();
            foreach (char c in s.toCharArray())
            {
                if (isWord(c))
                {
                    continue;
                }
                set.add(c);
            }
            set.add((char)0);
            set.add((char)0x09);
            set.add((char)8203);
            // http://www.unicode-symbol.com/block/Punctuation.html
            for (int i = 0x2000; i <= 0x206F; i++)
            {
                set.add((char)i);
            }
            set.add((char)0x0E00);//Thai

            //https://unicode-table.com/en/blocks/arabic/
            //Punctuation Arabic
            set.add((char)0x060C);
            set.add((char)0x060D);
            set.add((char)0x061B);
            set.add((char)0x061E);
            set.add((char)0x061F);
            set.add((char)0x06D4);

            //Devanagari
            set.add((char)0x0964);
            set.add((char)0x0965);
        }

        public bool isWord(char c)
        {
            // https://unicode-table.com/en/blocks/basic-latin/
            // 0-9
            if (c >= 0x30 && c <= 0x39)
            {
                return true;
            }
            // A - Z
            if (c >= 0x41 && c <= 0x5A)
            {
                return true;
            }
            // a - z
            if (c >= 0x61 && c <= 0x7A)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/latin-1-supplement/
            if (c >= 0xC0 && c <= 0xFF)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/latin-extended-a/
            if (c >= 0x0100 && c <= 0x017F)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/latin-extended-b/
            if (c >= 0x0180 && c <= 0x024F)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/ipa-extensions/
            if (c >= 0x0250 && c <= 0x02AF)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/combining-diacritical-marks/
            if (c >= 0x0300 && c <= 0x036F)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/greek-coptic/
            if (c >= 0x0370 && c <= 0x03FF)
            {
                return true;
            }

            //Russian
            // https://unicode-table.com/en/blocks/cyrillic/
            // https://unicode-table.com/en/blocks/cyrillic-supplement/
            if (c >= 0x0400 && c <= 0x052F)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/armenian/
            if (c >= 0x0530 && c <= 0x058F)
            {
                return true;
            }

            if (isWordRight2Left(c))
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/devanagari/
            // India
            if (c >= 0x0900 && c <= 0x097F)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/hangul-jamo/
            if (c >= 0x1100 && c <= 0x11FF)
            {
                return true;
            }
            //https://unicode-table.com/en/blocks/hangul-jamo-extended-b/
            if (c >= 0xD7B0 && c <= 0xD7FF)
            {
                return true;
            }
            //Japanese
            /*
            if (c >= 0x3040 && c <= 0x312F)
            {
                return true;
            }
            */

            // https://unicode-table.com/en/blocks/latin-extended-additional/
            if (c >= 0x1E00 && c <= 0x1EFF)
            {
                return true;
            }
            // https://unicode-table.com/en/blocks/greek-extended/
            if (c >= 0x1F00 && c <= 0x1FFF)
            {
                return true;
            }

            //special
            return c == '-' || c == '#';
        }

        private bool isWordRight2Left(char c)
        {
            // https://unicode-table.com/en/blocks/hebrew/
            // https://www.compart.com/en/unicode/block/U+0590
            if (c >= 0x0590 && c <= 0x05FF)
            {
                return true;
            }
            // https://unicode-table.com/en/blocks/arabic/
            // https://www.compart.com/en/unicode/bidiclass/AL
            if (c >= 0x0600 && c <= 0x06FF)
            {
                return true;
            }

            // https://unicode-table.com/en/blocks/arabic-supplement/
            if (c >= 0x0750 && c <= 0x077F)
            {
                return true;
            }
            // https://unicode-table.com/en/blocks/arabic-extended-a/
            if (c >= 0x08A0 && c <= 0x08FF)
            {
                return true;
            }

            return false;
        }

        public char[] clear(String str)
        {
            char[] cs = (str + "   ").toLowerCase().toCharArray();
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] == '"')
                {
                    continue;
                }
                if (set.contains(cs[i]))
                {
                    cs[i] = ' ';
                }
            }
            return cs;
        }

        public ArrayList<KeyWord> fromString(long id, char[] str, bool forIndex)
        {

            ArrayList<KeyWord> kws = new ArrayList<KeyWord>();

            KeyWordE k = null;
            int linkedCount = 0;
            int lastNPos = -2;
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (c == ' ')
                {
                    if (k != null)
                    {
                        kws.add(k);
                    }
                    k = null;

                }
                else if (c == '"')
                {
                    if (k != null)
                    {
                        kws.add(k);
                    }
                    k = null;

                    if (linkedCount > 0)
                    {
                        linkedCount = 0;
                        setLinkEnd(kws);
                    }
                    else
                    {
                        linkedCount = 1;
                    }
                }
                else if (isWord(c))
                {
                    if (k == null && c != '-' && c != '#')
                    {
                        k = new KeyWordE();
                        k.I = id;
                        k.setKeyWord("");
                        k.P = i;
                        if (linkedCount > 0)
                        {
                            linkedCount++;
                        }
                        if (linkedCount > 2)
                        {
                            k.isLinked = true;
                        }
                    }
                    if (k != null)
                    {
                        k.setKeyWord(k.getKeyWord() + c.ToString());
                    }
                }
                else
                {
                    if (k != null)
                    {
                        kws.add(k);
                    }
                    k = null;

                    KeyWordN n = new KeyWordN();
                    n.I = id;
                    n.P = i;
                    n.longKeyWord(c, (char)0, (char)0);
                    n.isLinked = i == (lastNPos + 1);
                    kws.add(n);

                    char c1 = str[i + 1];
                    if ((c1 != ' ' && c1 != '"') && (!isWord(c1)))
                    {
                        n = new KeyWordN();
                        n.I = id;
                        n.P = i;
                        n.longKeyWord(c, c1, (char)0);
                        n.isLinked = i == (lastNPos + 1);
                        kws.add(n);
                        if (!forIndex)
                        {
                            kws.remove(kws.size() - 2);
                            i++;
                        }
                    }

                    if (c1 == ' ' || c1 == '"')
                    {
                        setLinkEnd(kws);
                    }

                    lastNPos = i;

                }
            }
            setLinkEnd(kws);
            return kws;
        }

        private void setLinkEnd(ArrayList<KeyWord> kws)
        {
            if (kws.size() > 1)
            {
                KeyWord last = kws.get(kws.size() - 1);
                if (last.isLinked)
                {
                    last.isLinkedEnd = true;
                }
            }
        }

        public String getDesc(String str, KeyWord kw, int length)
        {
            ArrayList<KeyWord> list = new ArrayList<KeyWord>();
            while (kw != null)
            {
                list.add(kw);
                kw = kw.previous;
            }

            KeyWord[] ps = list.toArray();
            Array.Sort(ps, (KeyWord o1, KeyWord o2) =>
            {
                return o1.P - o2.P;
            });


            int start = -1;
            int end = -1;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ps.Length; i++)
            {
                int len = ps[i] is KeyWordE ? ps[i].getKeyWord()
                    .ToString().length() : ((KeyWordN)ps[i]).size();
                start = ps[i].P;
                if ((start + len) <= end)
                {
                    continue;
                }
                if ((start + len) >= str.length())
                {
                    continue;
                }

                end = start + length;
                if (end > str.length())
                {
                    end = str.length();
                }
                sb.append(str.substring(start, end))
                    .append("...");
            }
            return sb.ToString();

        }
    }
    #endregion
    #region KeyWord
    public abstract class KeyWord
    {

        public readonly static int MAX_WORD_LENGTH = 16;

        public static void config(DatabaseConfig c)
        {
            // English Language or Word (max=16)              
            c.EnsureTable<KeyWordE>("/E", "K(" + MAX_WORD_LENGTH + ")", "I", "P");

            // Non-English Language or Character
            c.EnsureTable<KeyWordN>("/N", "K", "I", "P");

        }

        public abstract Object getKeyWord();

        public abstract void setKeyWord(Object k);

        public abstract int size();
        //Position
        public int P;

        //Document ID
        public long I;

        [NotColumn]
        public KeyWord previous;
        [NotColumn]
        public bool isLinked;
        [NotColumn]
        public bool isLinkedEnd;

        public String ToFullString()
        {
            return (previous != null ? previous.ToFullString() + " -> " : "") + ToString();
        }


    }

    public sealed class KeyWordE : KeyWord
    {
        //Key Word
        public String K;

        public override Object getKeyWord()
        {
            return K;
        }

        public override void setKeyWord(Object k)
        {
            String t = (String)k;
            if (t.length() > KeyWord.MAX_WORD_LENGTH)
            {
                return;
            }
            K = t;
        }

        public override int size()
        {
            return K.length();
        }

        public override String ToString()
        {
            return K + " Pos=" + P + ", ID=" + I + " E";
        }
    }

    public sealed class KeyWordN : KeyWord
    {
        //Key Word 
        public long K;

        public override Object getKeyWord()
        {
            return K;
        }

        public override void setKeyWord(Object k)
        {
            K = (long)k;
        }

        public override int size()
        {
            if ((K & CMASK) != 0L)
            {
                return 3;
            }
            if ((K & (CMASK << 16)) != 0L)
            {
                return 2;
            }
            return 1;
        }

        const long CMASK = 0xFFFF;

        private static String KtoString(long k)
        {
            char c0 = (char)((k & (CMASK << 32)) >> 32);
            char c1 = (char)((k & (CMASK << 16)) >> 16);
            char c2 = (char)(k & CMASK);

            if (c2 != 0)
            {
                return new String(new char[] { c0, c1, c2 });
            }
            if (c1 != 0)
            {
                return new String(new char[] { c0, c1 });
            }
            return c0.ToString();
        }

        public void longKeyWord(char c0, char c1, char c2)
        {
            long k = (0L | c0) << 32;
            if (c1 != 0)
            {
                k |= ((0L | c1) << 16);
                if (c2 != 0)
                {
                    k |= (0L | c2);
                }
            }
            K = k;
        }

        public String toKString()
        {
            return KtoString(K);
        }

        public override String ToString()
        {
            return toKString() + " Pos=" + P + ", ID=" + I + " N";
        }
    }
    #endregion
    #region Transplanting Bridge
    internal static class CSharpBridge
    {
        public static String toString<T>(this T o)
        {
            return o.ToString();
        }
        public static bool equals<T>(this T o, T o2)
        {
            return o.Equals(o2);
        }

        public static int length(this StringBuilder self)
        {
            return self.Length;
        }

        public static char charAt(this StringBuilder self, int pos)
        {
            return self[pos];
        }

        public static void insert(this StringBuilder self, int pos, char c)
        {
            self.Insert(pos, c);
        }
        public static bool isEmpty(this String self)
        {
            return self.Length == 0;
        }
        public static int length(this String self)
        {
            return self.Length;
        }

        public static char[] toCharArray(this String self)
        {
            return self.ToCharArray();
        }

        public static string toLowerCase(this String self)
        {
            return self.ToLower();
        }

        public static string substring(this String self, int start, int end)
        {
            return self.Substring(start, end - start);
        }
        public static string substring(this String self, int start)
        {
            return self.Substring(start);
        }
        public static string trim(this string self)
        {
            return self.Trim();
        }
        public static char charAt(this string self, int index)
        {
            return self[index];
        }
        public static int lastIndexOf(this string self, char c, int index)
        {
            return self.LastIndexOf(c, index);
        }
        public static int nextInt(this Random self, int value)
        {
            return self.Next(value);
        }

        public static bool add<T>(this HashSet<T> self, T v)
        {
            return self.Add(v);
        }
        public static bool remove<T>(this HashSet<T> self, T v)
        {
            return self.Remove(v);
        }
        public static bool contains<T>(this HashSet<T> self, T v)
        {
            return self.Contains(v);
        }

        public static void remove<T>(this ArrayList<T> self, int pos)
        {
            self.RemoveAt(pos);
        }

        public static int size<T>(this ArrayList<T> self)
        {
            return self.Count;
        }

        public static T[] toArray<T>(this ArrayList<T> self)
        {
            return self.ToArray();
        }

        public static T get<T>(this ArrayList<T> self, int pos)
        {
            return self[pos];
        }

        public static void add<T>(this ConcurrentQueue<T> self, T obj)
        {
            self.Enqueue(obj);
        }
        public static void remove<T>(this ConcurrentQueue<T> self)
        {
            T o;
            self.TryDequeue(out o);
        }
        public static int size<T>(this ConcurrentQueue<T> self)
        {
            return self.Count;
        }
        public static StringBuilder append(this StringBuilder self, string str)
        {
            return self.Append(str);
        }
    }

    internal class ArrayList<T> : List<T>
    {
        public bool isEmpty()
        {
            return this.Count == 0;
        }

        public void add(T t)
        {
            this.Add(t);
        }

        public void add(int index, T p)
        {
            this.Insert(index, p);
        }
    }

    internal class EngineIterator<T> : Iterator<T>
    {
    }

    internal class Iterator<T> : IEnumerator<T>
    {
        public delegate bool MoveNextDelegate();

        public delegate T CurrentDelegate();

        public MoveNextDelegate hasNext;
        public CurrentDelegate next;

        public bool MoveNext()
        {
            return hasNext();
        }

        public T Current
        {
            get
            {
                return next();
            }
        }

        void System.Collections.IEnumerator.Reset()
        {

        }

        object System.Collections.IEnumerator.Current
        {
            get
            {
                return this.Current;
            }
        }

        void IDisposable.Dispose()
        {

        }
    }

    internal class Iterable<T> : IEnumerable<T>
    {


        public IEnumerator<T> GetEnumerator()
        {
            return iterator;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return iterator;
        }

        public EngineIterator<T> iterator;
    }

    internal class Arrays
    {
        public static T[] copyOf<T>(T[] kws, int len)
        {
            T[] condition = new T[kws.Length - 1];
            Array.Copy(kws, 0, condition, 0, condition.Length);
            return condition;
        }
    }
    #endregion
}

