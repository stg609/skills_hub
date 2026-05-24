export declare const DEFAULT_CATEGORY: "默认分类";

export type SkillMetadata = {
  name: string;
  description: string;
};

export type SkillSourceInput = {
  projectId: number;
  repoUrl: string;
  defaultBranch: string;
  skillPath: string;
  commitSha: string;
  updatedAt: string;
  metadata: SkillMetadata;
};

export type SkillRecord = {
  identity: string;
  slug: string;
  name: string;
  description: string;
  category: string;
  artifactType: "skill";
  source: {
    provider: "gitlab";
    projectId: number;
    repoUrl: string;
    defaultBranch: string;
    skillDir: string;
    skillPath: string;
    commitSha: string;
  };
  updatedAt: string;
  installCommand: string;
  downloads: {
    d1: number;
    d7: number;
    all: number;
  };
  likes: number;
  active?: boolean;
  indexedAt?: string;
  missingSyncCount?: number;
};

export declare function buildSkillRecord(source: SkillSourceInput): SkillRecord;
export declare function sortSkillCards<T extends { downloads?: { d1?: number; d7?: number; all?: number }; updatedAt: string }>(cards: T[], sortMode?: string): T[];
export declare function toggleAnonymousLike(
  existingLikes: Array<{ skillSlug: string; visitorId: string; createdAt: string }>,
  skillSlug: string,
  visitorId: string
): {
  liked: boolean;
  likes: Array<{ skillSlug: string; visitorId: string; createdAt: string }>;
};
