import type {
  ComponentSnapshot,
} from '../types'

interface InstallationOption { id: string; name: string; components: ComponentSnapshot[] }

export function groupComponentsByInstallation(components: ComponentSnapshot[]): InstallationOption[] {
  const groups = new Map<string, InstallationOption>()
  for (const component of components) {
    let group = groups.get(component.installationId)
    if (!group) {
      group = { id: component.installationId, name: component.installationName, components: [] }
      groups.set(component.installationId, group)
    }
    group.components.push(component)
  }
  return [...groups.values()]
}
