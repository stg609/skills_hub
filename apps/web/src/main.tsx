import React from "react";
import { createRoot } from "react-dom/client";
import "./styles.css";

type Skill = {
  slug: string;
  name: string;
  description: string;
  category: string;
  repoPath: string;
  updatedAt: string;
  updatedLabel: string;
  installCommand: string;
  trackedInstallCommand?: string;
  downloads: {
    d1: number;
    d7: number;
    all: number;
  };
  likes: number;
  color: string;
};

const skills: Skill[] = [
  {
    slug: "codex-review-pack",
    name: "codex-review-pack",
    description: "Pull request review, CI failure triage, and risky diff inspection for coding agents.",
    category: "默认分类",
    repoPath: "agent-skills/dev-quality/codex-review-pack",
    updatedAt: "2026-05-23T09:18:00+08:00",
    updatedLabel: "今天 09:18",
    installCommand: "npx skills add git@gitlab.company.local:agent-skills/dev-quality.git --skill codex-review-pack",
    trackedInstallCommand: "npx @company/skills-hub add codex-review-pack --hub https://skills-hub.company.local",
    downloads: { d1: 22, d7: 128, all: 842 },
    likes: 96,
    color: "#2e68ff"
  },
  {
    slug: "frontend-taste-lab",
    name: "frontend-taste-lab",
    description: "Opinionated UI critique prompts, layout heuristics, and visual QA checklists.",
    category: "默认分类",
    repoPath: "agent-skills/product/frontend-taste-lab",
    updatedAt: "2026-05-22T17:42:00+08:00",
    updatedLabel: "昨天 17:42",
    installCommand: "npx skills add git@gitlab.company.local:agent-skills/product.git --skill frontend-taste-lab",
    trackedInstallCommand: "npx @company/skills-hub add frontend-taste-lab --hub https://skills-hub.company.local",
    downloads: { d1: 31, d7: 94, all: 621 },
    likes: 74,
    color: "#bd5932"
  },
  {
    slug: "meeting-notes-zh",
    name: "meeting-notes-zh",
    description: "Chinese meeting transcript cleanup, summary extraction, and action item formatting.",
    category: "默认分类",
    repoPath: "agent-skills/workflow/meeting-notes-zh",
    updatedAt: "2026-05-21T11:06:00+08:00",
    updatedLabel: "5月21日",
    installCommand: "npx skills add git@gitlab.company.local:agent-skills/workflow.git --skill meeting-notes-zh",
    trackedInstallCommand: "npx @company/skills-hub add meeting-notes-zh --hub https://skills-hub.company.local",
    downloads: { d1: 9, d7: 52, all: 412 },
    likes: 58,
    color: "#23c7b7"
  },
  {
    slug: "rust-tauri-repair",
    name: "rust-tauri-repair",
    description: "Debugging recipes for Tauri, Rust integration tests, Windows audio, and recovery flows.",
    category: "默认分类",
    repoPath: "agent-skills/native/rust-tauri-repair",
    updatedAt: "2026-05-19T15:30:00+08:00",
    updatedLabel: "5月19日",
    installCommand: "npx skills add git@gitlab.company.local:agent-skills/native.git --skill rust-tauri-repair",
    trackedInstallCommand: "npx @company/skills-hub add rust-tauri-repair --hub https://skills-hub.company.local",
    downloads: { d1: 6, d7: 48, all: 301 },
    likes: 41,
    color: "#b7ff2a"
  }
];

function App() {
  const initialCatalog = import.meta.env.DEV ? skills : [];
  const [catalog, setCatalog] = React.useState<Skill[]>(initialCatalog);
  const [query, setQuery] = React.useState("");
  const [sort, setSort] = React.useState("updated");
  const [page, setPage] = React.useState(1);
  const [total, setTotal] = React.useState(initialCatalog.length);
  const [selectedSlug, setSelectedSlug] = React.useState(initialCatalog[0]?.slug ?? "");
  const [likedSlugs, setLikedSlugs] = React.useState<Set<string>>(new Set());
  const [toast, setToast] = React.useState("");
  const [loadError, setLoadError] = React.useState("");

  React.useEffect(() => {
    const controller = new AbortController();
    fetch(`/api/skills?q=${encodeURIComponent(query)}&sort=${encodeURIComponent(sort)}&page=${page}&pageSize=30`, {
      signal: controller.signal
    })
      .then((response) => response.ok ? response.json() : Promise.reject(new Error("API unavailable")))
      .then((result: ApiSkillPage) => {
        const nextCatalog = result.items.map(fromApiSkill);
        setCatalog(nextCatalog);
        setTotal(result.total);
        setLoadError("");
        if (nextCatalog.length > 0 && !nextCatalog.some((skill) => skill.slug === selectedSlug)) {
          setSelectedSlug(nextCatalog[0].slug);
        }
      })
      .catch((error: Error) => {
        if (error.name === "AbortError") return;
        if (import.meta.env.DEV) {
          const fallback = sortLocalSkills(skills, query, sort);
          setCatalog(fallback);
          setTotal(fallback.length);
          setLoadError("");
          return;
        }

        setCatalog([]);
        setTotal(0);
        setLoadError("暂时无法连接 Skills Hub API");
      });

    return () => controller.abort();
  }, [query, sort, page]);

  React.useEffect(() => {
    setPage(1);
  }, [query, sort]);

  const filteredSkills = catalog;

  const selected = catalog.find((skill) => skill.slug === selectedSlug) ?? catalog[0];
  const selectedIsLiked = selected ? likedSlugs.has(selected.slug) : false;

  function flash(message: string) {
    setToast(message);
    window.setTimeout(() => setToast(""), 1500);
  }

  async function copyCommand() {
    if (!selected) return;
    await navigator.clipboard.writeText(selected.trackedInstallCommand || selected.installCommand);
    void fetch(`/api/skills/${selected.slug}/install`, { method: "POST" }).catch(() => undefined);
    flash("安装命令已复制");
  }

  async function toggleLike() {
    if (!selected) return;
    const visitorId = getVisitorId();
    const nextLiked = !selectedIsLiked;
    const response = await fetch(`/api/skills/${selected.slug}/like`, {
      method: nextLiked ? "POST" : "DELETE",
      headers: {
        "x-skills-hub-visitor": visitorId
      }
    }).catch(() => undefined);

    const result = response?.ok ? await response.json() as LikeResult : undefined;
    if (!result) {
      flash("点赞失败，请稍后重试");
      return;
    }

    setLikedSlugs((current) => {
      const next = new Set(current);
      if (nextLiked) next.add(selected.slug);
      else next.delete(selected.slug);
      return next;
    });

    setCatalog((current) => current.map((skill) => skill.slug === selected.slug ? { ...skill, likes: result.likes } : skill));

    if (nextLiked) {
      flash("已点赞");
    } else {
        flash("已取消点赞");
    }
  }

  function downloadZip() {
    if (!selected) return;
    window.open(`/api/skills/${selected.slug}/download`, "_blank", "noopener,noreferrer");
  }

  return (
    <div className="shell">
      <aside className="rail" aria-label="分类与同步状态">
        <section className="brand">
          <div className="brand-mark" aria-hidden="true" />
          <h1>Skills<br />Hub</h1>
          <p>从公司 GitLab 自动索引 coding agent skills。默认分类先跑起来，后续交给 LLM 整理。</p>
        </section>

        <div className="nav-title">Category</div>
        <nav className="category-list" aria-label="Skill 分类">
          <button className="category active" type="button"><span>默认分类</span><strong>{catalog.length}</strong></button>
        </nav>

        <section className="rail-card">
          <div className="sync-line"><span className="pulse" /><strong>{loadError ? "API ERROR" : "CATALOG"}</strong></div>
          <p>{loadError || `当前结果 ${catalog.length} 个，索引总量 ${total.toLocaleString()} 个。`}</p>
        </section>
      </aside>

      <main>
        <section className="topbar">
          <div className="headline">
            <span className="eyebrow">GITLAB INDEX / PUBLIC CATALOG</span>
            <h2>内部技能索引台</h2>
            <p>给 Codex 和其他 coding agent 找技能、装技能、看来源。列表保持公开，不登录也能下载、复制安装命令和点赞。</p>
          </div>
          <aside className="stats-strip" aria-label="总览数据">
            <div className="stat"><strong>{total}</strong><span>当前 skills</span></div>
            <div className="stat"><strong>{catalog.length}</strong><span>本页结果</span></div>
            <div className="stat"><strong>{page}</strong><span>当前页</span></div>
            <div className="stat"><strong>{loadError ? "异常" : "在线"}</strong><span>API 状态</span></div>
          </aside>
        </section>

        <section className="controls" aria-label="筛选和排序">
          <label className="search">
            <SearchIcon />
            <input
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              type="search"
              placeholder="搜索 skill 名称、描述或 GitLab 路径"
              aria-label="搜索 skill"
            />
          </label>
          <select value={sort} onChange={(event) => setSort(event.target.value)} aria-label="排序">
            <option value="updated">最后更新时间</option>
            <option value="downloads_1d">1d 下载次数</option>
            <option value="downloads_7d">1周下载次数</option>
            <option value="downloads_all">所有下载次数</option>
          </select>
          <div className="view-toggle" aria-label="视图切换">
            <button className="icon-btn active" type="button" aria-label="列表视图"><ListIcon /></button>
            <button className="icon-btn" type="button" aria-label="紧凑视图"><GridIcon /></button>
          </div>
        </section>

        <section className="skill-list" aria-label="Skill 列表">
          {loadError && <div className="empty-state">{loadError}</div>}
          {!loadError && filteredSkills.length === 0 && <div className="empty-state">没有找到匹配的 skill</div>}
          {filteredSkills.map((skill) => (
            <article
                className={`skill-row ${selected?.slug === skill.slug ? "selected" : ""}`}
              key={skill.slug}
              tabIndex={0}
              onClick={() => setSelectedSlug(skill.slug)}
              onKeyDown={(event) => event.key === "Enter" && setSelectedSlug(skill.slug)}
            >
              <div className="skill-main">
                <div className="skill-name">
                  <span className="source-dot" style={{ background: skill.color }} aria-hidden="true" />
                  <h3>{skill.name}</h3>
                </div>
                <p>{skill.description}</p>
              </div>
              <div className="cell"><span>最后更新</span><strong>{skill.updatedLabel}</strong></div>
              <div className="cell"><span>GitLab 路径</span><strong>{skill.repoPath.split("/").slice(-2).join("/")}</strong></div>
              <div className="cell"><span>下载</span><strong>{skill.downloads.all}</strong></div>
              <button className="row-action" type="button" aria-label={`查看 ${skill.name}`}><ArrowIcon /></button>
            </article>
          ))}
        </section>
        <section className="pager" aria-label="分页">
          <span>第 {page} 页 · 当前 {filteredSkills.length} 条 · 共 {total.toLocaleString()} 条</span>
          <div>
            <button type="button" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page === 1}>上一页</button>
            <button type="button" onClick={() => setPage((current) => current + 1)} disabled={page * 30 >= total}>下一页</button>
          </div>
        </section>
      </main>

      <aside className="drawer" aria-label="Skill 详情">
        {selected ? <section className="drawer-card">
          <div className="tag-row">
            <span className="tag">{selected.category}</span>
            <span className="tag">GitLab indexed</span>
          </div>
          <h2>{selected.name}</h2>
          <p>{selected.description}</p>
          <div className="command">
            <span>可统计安装</span>
            <code>{selected.trackedInstallCommand || selected.installCommand}</code>
          </div>
          <div className="command subtle">
            <span>原生 GitLab 安装</span>
            <code>{selected.installCommand}</code>
          </div>
          <div className="drawer-actions">
            <button className="primary" onClick={copyCommand} type="button">复制安装命令</button>
            <button className="secondary" onClick={downloadZip} type="button" aria-label="下载 zip">
              <DownloadIcon />
            </button>
            <button
              className={`secondary ${selectedIsLiked ? "liked" : ""}`}
              onClick={toggleLike}
              type="button"
              aria-label="点赞"
            >
              <LikeIcon />
            </button>
          </div>
          <div className="meta-grid">
            <div className="meta"><span>GitLab 路径</span><strong>{selected.repoPath}</strong></div>
            <div className="meta"><span>最后更新</span><strong>{selected.updatedLabel}</strong></div>
            <div className="meta"><span>下载</span><strong>{selected.downloads.all.toLocaleString()}</strong></div>
            <div className="meta"><span>点赞</span><strong>{selected.likes.toLocaleString()}</strong></div>
          </div>
        </section> : <section className="drawer-card empty-detail">选择一个 skill 查看详情</section>}
      </aside>

      <div className={`toast ${toast ? "show" : ""}`} role="status" aria-live="polite">{toast}</div>
    </div>
  );
}

function SearchIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></svg>;
}

function ListIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 6h13" /><path d="M8 12h13" /><path d="M8 18h13" /><path d="M3 6h.01" /><path d="M3 12h.01" /><path d="M3 18h.01" /></svg>;
}

function GridIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="7" height="7" /><rect x="14" y="3" width="7" height="7" /><rect x="3" y="14" width="7" height="7" /><rect x="14" y="14" width="7" height="7" /></svg>;
}

function ArrowIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14" /><path d="m13 6 6 6-6 6" /></svg>;
}

function LikeIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 10v11" /><path d="M15 5.9 14 10h5.8a2 2 0 0 1 2 2.3l-1.2 7A2 2 0 0 1 18.6 21H7a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2h2.8L13 4.6a1.2 1.2 0 0 1 2 .1c.2.3.2.7.1 1.2Z" /></svg>;
}

function DownloadIcon() {
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3v12" /><path d="m7 10 5 5 5-5" /><path d="M5 21h14" /></svg>;
}

type ApiSkill = {
  slug: string;
  name: string;
  description: string;
  category: string;
  source: {
    repoUrl: string;
    skillDir: string;
  };
  updatedAt: string;
  installCommand: string;
  trackedInstallCommand?: string;
  downloads: {
    d1: number;
    d7: number;
    all: number;
  };
  likes: number;
};

type ApiSkillPage = {
  items: ApiSkill[];
  page: number;
  pageSize: number;
  total: number;
  totalRelation: string;
};

type LikeResult = {
  slug: string;
  liked: boolean;
  likes: number;
};

function fromApiSkill(skill: ApiSkill): Skill {
  return {
    slug: skill.slug,
    name: skill.name,
    description: skill.description,
    category: skill.category,
    repoPath: `${repoPathLabel(skill.source.repoUrl)}/${skill.source.skillDir}`,
    updatedAt: skill.updatedAt,
    updatedLabel: formatUpdatedAt(skill.updatedAt),
    installCommand: skill.installCommand,
    trackedInstallCommand: skill.trackedInstallCommand,
    downloads: skill.downloads,
    likes: skill.likes,
    color: colorForSlug(skill.slug)
  };
}

function repoPathLabel(repoUrl: string) {
  const withoutGitSuffix = repoUrl.replace(/\.git$/, "");
  const sshMatch = withoutGitSuffix.match(/^[^@]+@[^:]+:(.+)$/);
  if (sshMatch) return sshMatch[1];

  try {
    const url = new URL(withoutGitSuffix);
    return url.pathname.replace(/^\/+/, "");
  } catch {
    return withoutGitSuffix;
  }
}

function sortLocalSkills(items: Skill[], query: string, sort: string) {
  const normalizedQuery = query.trim().toLowerCase();
  return items
    .filter((skill) => !normalizedQuery || `${skill.name} ${skill.description} ${skill.repoPath}`.toLowerCase().includes(normalizedQuery))
    .sort((left, right) => {
      if (sort === "downloads_1d") return right.downloads.d1 - left.downloads.d1;
      if (sort === "downloads_7d") return right.downloads.d7 - left.downloads.d7;
      if (sort === "downloads_all") return right.downloads.all - left.downloads.all;
      return new Date(right.updatedAt).getTime() - new Date(left.updatedAt).getTime();
    });
}

function formatUpdatedAt(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("zh-CN", {
    month: "numeric",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit"
  }).format(date);
}

function colorForSlug(slug: string) {
  const palette = ["#2e68ff", "#bd5932", "#23c7b7", "#b7ff2a"];
  const index = [...slug].reduce((sum, char) => sum + char.charCodeAt(0), 0) % palette.length;
  return palette[index];
}

function getVisitorId() {
  const key = "skills-hub-visitor-id";
  const existing = window.localStorage.getItem(key);
  if (existing) return existing;
  const next = crypto.randomUUID();
  window.localStorage.setItem(key, next);
  return next;
}

createRoot(document.getElementById("root")!).render(<App />);
