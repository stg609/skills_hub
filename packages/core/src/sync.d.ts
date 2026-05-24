export declare function applyIndexedSkills(
  existingSkills: any[],
  incomingSkills: any[],
  indexedAt: string
): {
  skills: any[];
  summary: {
    created: number;
    updated: number;
    deactivated: number;
    indexedAt: string;
  };
};
